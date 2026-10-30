using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WishHub.Api;
using Xunit;

namespace WishHub.Tests.Api;

[Trait("Category", "Integration")]
[Trait("Requires", "Infrastructure")]
public class AuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new
        {
            Username = $"testuser_{Guid.NewGuid()}",
            Email = $"test_{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            Password = "TestPassword123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateUsername_ReturnsConflict()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"duplicateuser_{Guid.NewGuid()}";
        var request = new
        {
            Username = username,
            Email = $"email_{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            Password = "TestPassword123!"
        };

        // First registration
        await client.PostAsJsonAsync("/api/auth/register", request);

        // Act - Second registration with same username
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"loginuser_{Guid.NewGuid()}";
        var registerRequest = new
        {
            Username = username,
            Email = $"login_{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            Password = "TestPassword123!"
        };

        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new
        {
            UsernameOrEmail = username,
            Password = "TestPassword123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<object>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var loginRequest = new
        {
            UsernameOrEmail = "nonexistent",
            Password = "WrongPassword123!"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewTokens()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"refreshuser_{Guid.NewGuid()}";
        var registerRequest = new
        {
            Username = username,
            Email = $"refresh_{Guid.NewGuid()}@example.com",
            DisplayName = "Test User",
            Password = "TestPassword123!"
        };

        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new
        {
            UsernameOrEmail = username,
            Password = "TestPassword123!"
        };

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var refreshRequest = new
        {
            RefreshToken = loginResult!.RefreshToken
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HubConnection_AuthenticationExpiry_ClosesTheExistingConnection()
    {
        // Start the actual Program pipeline before creating the short-lived local test credential.
        var websocketClient = _factory.Server.CreateWebSocketClient();
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var jwt = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"],
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddSeconds(4),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(configuration["Jwt:Secret"]!)), SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var socket = await websocketClient.ConnectAsync(
            new Uri("ws://localhost/hubs/chat?access_token=" + token), deadline.Token);
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
        await socket.SendAsync(handshake.AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
        var buffer = new byte[1024];
        var accepted = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        Assert.Equal(WebSocketMessageType.Text, accepted.MessageType);
        Assert.StartsWith("{}", Encoding.UTF8.GetString(buffer, 0, accepted.Count));

        var closed = false;
        try
        {
            while (!closed)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
                closed = received.MessageType == WebSocketMessageType.Close ||
                    Encoding.UTF8.GetString(buffer, 0, received.Count).Contains("\"type\":7", StringComparison.Ordinal);
            }
        }
        catch (WebSocketException)
        {
            closed = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Assert.Fail("An established connection remained open after its authentication expired.");
        }
        Assert.True(closed);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
}
