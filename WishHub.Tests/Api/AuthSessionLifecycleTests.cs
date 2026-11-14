using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using WishHub.Api.Controllers;
using WishHub.Api.Extensions;
using WishHub.Core.DTOs.Auth;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Api;

public sealed class AuthSessionLifecycleTests : IAsyncLifetime
{
    private const string Password = "Local-SessionTest123!";
    private const string CookieName = "wishhub-oauth-completion";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Func<Task>? _holdPasswordRequest;
    private bool _behindUnconfiguredProxy;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "Local-only-session-regression-secret-with-more-than-64-characters-123456",
            ["Jwt:Issuer"] = "WishHubTests", ["Jwt:Audience"] = "WishHubTestUsers",
            ["Jwt:AccessTokenExpiryMinutes"] = "7", ["Jwt:RefreshTokenExpiryDays"] = "30",
            ["Frontend:Url"] = "https://wishhub.example",
            ["ReverseProxy:KnownProxies"] = "192.0.2.10, 192.0.2.11",
            ["ReverseProxy:ForwardLimit"] = "2",
            ["OAuth:Vk:ClientId"] = "local-test-client", ["OAuth:Vk:ClientSecret"] = "local-test-only-secret",
            ["OAuth:Vk:RedirectUri"] = "https://wishhub.example/api/auth/vk/callback"
        }).Build();
        if (_behindUnconfiguredProxy)
        {
            configuration["ReverseProxy:Enabled"] = "true";
            configuration["ReverseProxy:KnownProxies"] = "";
        }
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddPasswordRequestLimits(configuration);
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
                services.AddIdentityCore<User>(options =>
                    {
                        options.Password.RequireNonAlphanumeric = false;
                        options.User.RequireUniqueEmail = true;
                    })
                    .AddEntityFrameworkStores<AppDbContext>();
                services.AddHttpClient();
                services.AddScoped<ITokenService, TokenService>();
                services.AddSingleton(new WishHub.Infrastructure.Storage.AvatarStorage(
                    Path.Combine(Path.GetTempPath(), "wishhub-auth-avatars-" + Guid.NewGuid().ToString("N"))));
                services.AddScoped<IUserService, UserService>();
                services.AddAuthentication("SessionTest")
                    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("SessionTest", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (IPAddress.TryParse(context.Request.Headers["X-Test-Peer"], out var peer))
                        context.Connection.RemoteIpAddress = peer;
                    await next(context);
                });
                if (!string.IsNullOrWhiteSpace(configuration["ReverseProxy:KnownProxies"]))
                    app.UseForwardedHeaders();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseRateLimiter();
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers.ContainsKey("X-Test-Hold") && _holdPasswordRequest is { } hold)
                        await hold();
                    await next(context);
                });
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();
        using (var scope = _host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Refresh_NormalSession_ConsumesOnceAndReportsActualJwtExpiry()
    {
        var session = await RegisterAsync();
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var next = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(session.User.Id, next.User.Id);
        Assert.NotEqual(session.RefreshToken, next.RefreshToken);
        Assert.Equal(new JwtSecurityTokenHandler().ReadJwtToken(next.AccessToken).ValidTo, next.ExpiresAt);
        Assert.InRange(next.ExpiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(8));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken })).StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.RefreshTokens.SingleAsync(token => token.Token == session.RefreshToken)).IsRevoked);
        Assert.False((await db.RefreshTokens.SingleAsync(token => token.Token == next.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task Complete_PendingCookie_RequiresHeaderConsumesOnceAndClearsSecureCookie()
    {
        var session = await RegisterAsync();
        var ticket = await PendingAsync(session.User.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(ticket, header: false)).StatusCode);
        using (var scope = _host.Services.CreateScope())
            Assert.False((await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens
                .SingleAsync(token => token.Token == ticket)).IsRevoked);

        var result = await CompleteAsync(ticket);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var completed = (await result.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(session.User.Id, completed.User.Id);
        Assert.False(completed.RefreshToken.StartsWith("oauth.", StringComparison.Ordinal));
        Assert.NotEmpty(completed.AccessToken);
        Assert.True(result.Headers.CacheControl?.NoStore);
        var cookie = SetCookieHeaderValue.Parse(Assert.Single(result.Headers.GetValues("Set-Cookie")));
        Assert.Equal(CookieName, cookie.Name.Value);
        Assert.Equal("/api/auth/complete", cookie.Path.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        Assert.True(cookie.MaxAge is null || cookie.MaxAge <= TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(ticket)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("ordinary-refresh")]
    public async Task Complete_UnavailableCookie_DoesNotIssueSession(string state)
    {
        var session = await RegisterAsync();
        var ticket = state switch
        {
            "missing" => null,
            "ordinary-refresh" => session.RefreshToken,
            "unknown" => "oauth.unknown-local-ticket",
            _ => await PendingAsync(session.User.Id, state)
        };
        int before;
        using (var scope = _host.Services.CreateScope())
            before = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.CountAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(ticket)).StatusCode);
        using (var scope = _host.Services.CreateScope())
            Assert.Equal(before, await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Refresh_PendingCompletionPurpose_IsNotAnOrdinaryRefresh()
    {
        var session = await RegisterAsync();
        var ticket = await PendingAsync(session.User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = ticket })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(ticket)).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesOwnSessionsAndPendingCookie_PreservesAnotherUser()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        var pending = await PendingAsync(first.User.Id);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-Test-User", first.User.Id.ToString());
        var logout = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        AssertPendingCookiesDeleted(logout);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(pending)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { first.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { second.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Success_RevokesOwnSessionsAndRequiresNewPassword()
    {
        var first = await RegisterAsync();
        var other = await RegisterAsync();
        var ticket = await PendingAsync(first.User.Id);
        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/users/me/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest { CurrentPassword = Password, NewPassword = "Local-NewPassword456!" })
        };
        change.Headers.Add("X-Test-User", first.User.Id.ToString());
        var changed = await _client.SendAsync(change);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        AssertPendingCookiesDeleted(changed);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { first.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteAsync(ticket)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = first.User.Username, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = first.User.Username, password = "Local-NewPassword456!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { other.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_KeepsExistingSession()
    {
        var session = await RegisterAsync();
        using (var scope = _host.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<IUserService>().ChangePasswordAsync(session.User.Id,
                new ChangePasswordRequest { CurrentPassword = "Wrong-Password123!", NewPassword = "Local-NewPassword456!" }));
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsJsonAsync("/api/auth/refresh", new { session.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task VkAuthorize_OnlyStartsFlow_UsesSecureStateCookiesAndClearsStaleLink()
    {
        // Only local initiation: no request to a VK endpoint and no provider callback is executed.
        var response = await _client.GetAsync("/api/auth/vk");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://id.vk.com/authorize", response.Headers.Location!.GetLeftPart(UriPartial.Path));
        var cookies = response.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value)).ToArray();
        Assert.Equal(3, cookies.Length);
        foreach (var cookie in cookies)
        {
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
            Assert.Equal("/", cookie.Path.Value);
        }
        Assert.True(Assert.Single(cookies, cookie => cookie.Name == "vk_oauth_state").Expires > DateTimeOffset.UtcNow);
        Assert.True(Assert.Single(cookies, cookie => cookie.Name == "vk_oauth_verifier").Expires > DateTimeOffset.UtcNow);
        Assert.True(Assert.Single(cookies, cookie => cookie.Name == "vk_oauth_link_user").Expires < DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("login", 12)]
    [InlineData("register", 5)]
    public async Task PasswordEndpoints_RequestsFromSamePeer_AreLimitedWithoutTrustingClientHeader(string endpoint, int limit)
    {
        for (var attempt = 0; attempt < limit; attempt++)
        {
            using var permitted = await InvalidPasswordRequestAsync(endpoint, "198.51.100.10", $"203.0.113.{attempt + 1}");
            Assert.Equal(HttpStatusCode.BadRequest, permitted.StatusCode);
        }

        using var rejected = await InvalidPasswordRequestAsync(endpoint, "198.51.100.10", "203.0.113.250");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0, 1, 60);
        Assert.True(rejected.Headers.CacheControl?.NoStore);
        using var separateClient = await InvalidPasswordRequestAsync(endpoint, "198.51.100.11", "203.0.113.250");
        Assert.Equal(HttpStatusCode.BadRequest, separateClient.StatusCode);
        using var otherEndpoint = await InvalidPasswordRequestAsync(endpoint == "login" ? "register" : "login", "198.51.100.10");
        Assert.Equal(HttpStatusCode.BadRequest, otherEndpoint.StatusCode);
    }

    [Fact]
    public async Task PasswordEndpoints_TwoTrustedProxies_UseClientAddressAndIgnoreEarlierValues()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var permitted = await InvalidPasswordRequestAsync("register", "192.0.2.10",
                $"203.0.113.{attempt + 1}, 198.51.100.10, 192.0.2.11");
            Assert.Equal(HttpStatusCode.BadRequest, permitted.StatusCode);
        }

        using var rejected = await InvalidPasswordRequestAsync("register", "192.0.2.10",
            "203.0.113.250, 198.51.100.10, 192.0.2.11");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var otherClient = await InvalidPasswordRequestAsync("register", "192.0.2.10",
            "203.0.113.250, 198.51.100.11, 192.0.2.11");
        Assert.Equal(HttpStatusCode.BadRequest, otherClient.StatusCode);
    }

    [Fact]
    public async Task Registration_Ipv6AddressesInOnePrefix_ShareQuota()
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var permitted = await InvalidPasswordRequestAsync("register", $"2001:db8:1:2::{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, permitted.StatusCode);
        }

        using var rejected = await InvalidPasswordRequestAsync("register", "2001:db8:1:2::6");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var otherPrefix = await InvalidPasswordRequestAsync("register", "2001:db8:1:3::1");
        Assert.Equal(HttpStatusCode.BadRequest, otherPrefix.StatusCode);
    }

    [Fact]
    public async Task PasswordEndpoints_EightRequestsInFlight_RejectNextAndReleaseSharedPermit()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        _holdPasswordRequest = async () =>
        {
            if (Interlocked.Increment(ref count) == 8)
                entered.TrySetResult();
            await release.Task.WaitAsync(deadline.Token);
        };
        var inFlight = Enumerable.Range(1, 8).Select(index => InvalidPasswordRequestAsync(
            index % 2 == 0 ? "login" : "register", $"198.51.100.{index}", hold: true)).ToArray();
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            using var rejected = await InvalidPasswordRequestAsync("login", "198.51.100.90").WaitAsync(deadline.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), rejected.Headers.RetryAfter?.Delta);
            using var unrelated = await _client.GetAsync("/api/auth/providers", deadline.Token);
            Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
        }
        finally
        {
            release.TrySetResult();
            foreach (var response in await Task.WhenAll(inFlight))
            {
                using (response)
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        using var available = await InvalidPasswordRequestAsync("login", "198.51.100.90");
        Assert.Equal(HttpStatusCode.BadRequest, available.StatusCode);
    }

    [Fact]
    public async Task Registration_UnconfiguredProxy_SharesDeploymentQuotaWithoutTrustingForwardedHeaders()
    {
        // Recreate this fixture's host with the same SQLite database and without a proxy allowlist.
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _behindUnconfiguredProxy = true;
        await InitializeAsync();
        for (var index = 1; index <= 20; index++)
        {
            using var permitted = await InvalidPasswordRequestAsync("register", "198.51.100.10", $"203.0.113.{index}");
            Assert.Equal(HttpStatusCode.BadRequest, permitted.StatusCode);
        }

        using var rejected = await InvalidPasswordRequestAsync("register", "198.51.100.11", "203.0.113.21");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private async Task<HttpResponseMessage> InvalidPasswordRequestAsync(string endpoint, string peer, string? forwarded = null,
        bool hold = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{endpoint}")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Test-Peer", peer);
        if (hold)
            request.Headers.Add("X-Test-Hold", "1");
        if (forwarded is not null)
            request.Headers.Add("X-Forwarded-For", forwarded);
        return await _client.SendAsync(request);
    }

    private static void AssertPendingCookiesDeleted(HttpResponseMessage response)
    {
        var cookies = response.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value)).ToArray();
        Assert.Equal(new[] { "vk_oauth_link_user", "vk_oauth_state", "vk_oauth_verifier", CookieName }.Order(),
            cookies.Select(cookie => cookie.Name.Value).Order());
        Assert.All(cookies, cookie =>
        {
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
            Assert.True(cookie.MaxAge is null || cookie.MaxAge <= TimeSpan.Zero);
        });
    }

    private async Task<AuthResponse> RegisterAsync()
    {
        var name = "session_" + Guid.NewGuid().ToString("N");
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            username = name, email = name + "@example.test", displayName = "Session tests", password = Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> PendingAsync(Guid ownerId, string state = "valid")
    {
        using var scope = _host.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateRefreshToken(ownerId);
        token.Token = "oauth." + token.Token;
        token.ExpiresAt = state == "expired" ? DateTime.UtcNow.AddSeconds(-1) : token.CreatedAt.AddMinutes(1);
        token.IsRevoked = state == "revoked";
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token.Token;
    }

    private Task<HttpResponseMessage> CompleteAsync(string? cookie, bool header = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/complete");
        if (header) request.Headers.Add("X-WishHub-OAuth", "1");
        if (cookie is not null) request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        return _client.SendAsync(request);
    }

    private sealed class SessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
