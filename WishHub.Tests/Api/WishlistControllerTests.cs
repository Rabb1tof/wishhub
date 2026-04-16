using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WishHub.Api;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using Xunit;

namespace WishHub.Tests.Api;

[Trait("Category", "Integration")]
[Trait("Requires", "Infrastructure")]
public class WishlistControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WishlistControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> AuthenticateAsync(HttpClient client)
    {
        var username = $"testuser_{Guid.NewGuid()}";
        var registerRequest = new
        {
            Username = username,
            Email = $"{username}@example.com",
            DisplayName = "Test User",
            Password = "TestPassword123!"
        };

        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new
        {
            UsernameOrEmail = username,
            Password = "TestPassword123!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result!.AccessToken;
    }

    private async Task SeedProductAsync(string url)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Products.AnyAsync(p => p.Url == url))
        {
            return;
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Url = url,
            Name = "Test Product",
            ImageUrl = "https://example.com/image.jpg",
            Price = 199m,
            Currency = "RUB",
            Source = ProductSource.Wildberries,
            CreatedAt = DateTime.UtcNow,
            LastParsedAt = DateTime.UtcNow
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetWishlist_WithAuth_ReturnsItems()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/wishlist");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetWishlist_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/wishlist");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_WithAuth_ReturnsCreated()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            Url = "https://www.wildberries.ru/catalog/310980140/detail.aspx"
        };

        await SeedProductAsync(request.Url);

        // Act
        var response = await client.PostAsJsonAsync("/api/wishlist", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_WithAuth_ReturnsNoContent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // First add an item
        var addRequest = new
        {
            Url = "https://www.wildberries.ru/catalog/310980140/detail.aspx"
        };
        await SeedProductAsync(addRequest.Url);
        var addResponse = await client.PostAsJsonAsync("/api/wishlist", addRequest);
        var addItem = await addResponse.Content.ReadFromJsonAsync<WishlistItemResponse>();

        // Act
        var response = await client.DeleteAsync($"/api/wishlist/{addItem!.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReserveItem_WithAuth_ReturnsSuccess()
    {
        // Arrange
        var ownerClient = _factory.CreateClient();
        var ownerToken = await AuthenticateAsync(ownerClient);
        ownerClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ownerToken);

        var addRequest = new
        {
            Url = "https://www.wildberries.ru/catalog/310980140/detail.aspx"
        };
        await SeedProductAsync(addRequest.Url);
        var addResponse = await ownerClient.PostAsJsonAsync("/api/wishlist", addRequest);
        var addedItem = await addResponse.Content.ReadFromJsonAsync<WishlistItemResponse>();

        // Reserve by another user
        var reserverClient = _factory.CreateClient();
        var reserverToken = await AuthenticateAsync(reserverClient);
        reserverClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reserverToken);

        var response = await reserverClient.PostAsync($"/api/wishlist/{addedItem!.Id}/reserve", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
    private record WishlistItemResponse(Guid Id);
}
