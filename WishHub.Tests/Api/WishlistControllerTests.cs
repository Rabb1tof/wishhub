using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WishHub.Api.Controllers;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Tests.Infrastructure;

namespace WishHub.Tests.Api;

// Real HTTP routing/model binding/authentication over TestServer, with only the wishlist stack.
// Program startup independently requires the production PostgreSQL integration.
public class WishlistControllerTests : IAsyncLifetime
{
    private readonly WishlistTestStore _store = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_store.Connection));
                services.AddSingleton<IWishlistImageStorage>(_store.Images);
                services.AddScoped<IWishlistService, WishlistService>();
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(WishlistController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _store.DisposeAsync();
    }

    private async Task<(HttpClient Client, User User)> AuthenticatedAsync(WishlistPrivacy privacy = WishlistPrivacy.Public)
    {
        var user = await _store.AddUserAsync(privacy);
        var client = _host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.Id.ToString());
        return (client, user);
    }

    [Fact]
    public async Task AddItem_OnlyUrlAndName_ReturnsCreatedFlatContractWithoutProduct()
    {
        var (client, _) = await AuthenticatedAsync();
        var response = await client.PostAsJsonAsync("/api/wishlist", new { url = " HTTPS://SHOP.EXAMPLE/p?sku=5 ", name = " Gift " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "createdAt", "currency", "id", "imageUrl", "isReserved", "isReservedByMe", "name", "price", "reservedBy", "sortOrder", "source", "url" },
            json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Gift", json.GetProperty("name").GetString());
        Assert.Equal("https://shop.example/p?sku=5", json.GetProperty("url").GetString());
        Assert.Equal("RUB", json.GetProperty("currency").GetString());
        Assert.Equal("Unknown", json.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("price").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("imageUrl").ValueKind);
        Assert.False(json.GetProperty("isReserved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reservedBy").ValueKind);
        Assert.Empty(await _store.Db.Products.ToListAsync());
        var items = await client.GetFromJsonAsync<WishlistItemDto[]>("/api/wishlist");
        Assert.Single(items!);
        Assert.Equal(json.GetProperty("id").GetGuid(), items![0].Id);
    }

    [Fact]
    public async Task Save_InvalidPayloads_ReturnsBadRequestWithoutWrites()
    {
        var (client, _) = await AuthenticatedAsync();
        var invalidPayloads = new object[]
        {
            new { url = "https://shop.example/p" },
            new { url = "https://shop.example/p", name = "   " },
            new { url = "ftp://shop.example/p", name = "Gift" },
            new { url = "/p", name = "Gift" },
            new { url = "https://shop.example/p", name = "Gift", price = -0.01m },
            new { url = "https://shop.example/p", name = "Gift", imageUrl = "data:image/png;base64,abc" },
            new { url = "https://shop.example/" + new string('x', 2000), name = "Gift" },
            new { url = "https://shop.example/p", name = new string('n', 501) }
        };
        foreach (var payload in invalidPayloads)
        {
            var response = await client.PostAsJsonAsync("/api/wishlist", payload);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(await _store.Db.WishlistItems.ToListAsync());
    }

    [Fact]
    public async Task UpdateItem_FullReplacement_ClearsOptionalsAndKeepsSortOrder()
    {
        var (client, _) = await AuthenticatedAsync();
        var created = await client.PostAsJsonAsync("/api/wishlist", new
        {
            url = "https://shop.example/p", name = "Gift", imageUrl = "https://cdn.example/one.jpg", price = 100m
        });
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        var replacement = await client.PutAsJsonAsync("/api/wishlist/" + card.Id, new
        {
            url = "https://www.wildberries.ru/catalog/2/detail.aspx?size=3", name = "Updated",
            imageUrl = "https://cdn.example/two.jpg", price = 0m, sortOrder = 42
        });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var updated = (await replacement.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal(0m, updated.Price);
        Assert.Equal("Wildberries", updated.Source);
        Assert.Equal("https://cdn.example/two.jpg", updated.ImageUrl);
        var clearedResponse = await client.PutAsJsonAsync("/api/wishlist/" + card.Id,
            new { url = updated.Url, name = "Cleared", imageUrl = (string?)null, price = (decimal?)null });
        Assert.Equal(HttpStatusCode.OK, clearedResponse.StatusCode);
        var cleared = (await clearedResponse.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal("Cleared", cleared.Name);
        Assert.Null(cleared.ImageUrl);
        Assert.Null(cleared.Price);
        Assert.Equal(42, cleared.SortOrder);
        Assert.Equal(card.CreatedAt, cleared.CreatedAt);
        var invalid = await client.PutAsJsonAsync("/api/wishlist/" + card.Id, new { name = "Missing URL" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Save_DuplicateUrl_ReturnsConflictOnAddAndUpdate()
    {
        var (client, _) = await AuthenticatedAsync();
        var first = await client.PostAsJsonAsync("/api/wishlist", new { url = "https://shop.example/a", name = "A" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsJsonAsync("/api/wishlist", new { url = "https://shop.example/b", name = "B" });
        var card = (await second.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        var duplicateAdd = await client.PostAsJsonAsync("/api/wishlist", new { url = " HTTPS://SHOP.EXAMPLE/a ", name = "Duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateAdd.StatusCode);
        var duplicateEdit = await client.PutAsJsonAsync("/api/wishlist/" + card.Id, new { url = "https://shop.example/a", name = "Duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateEdit.StatusCode);
        var unchanged = await client.GetFromJsonAsync<WishlistItemDto[]>("/api/wishlist");
        Assert.Equal(new[] { "A", "B" }, unchanged!.Select(c => c.Name));
    }

    [Fact]
    public async Task UpdateAndDelete_NonOwner_CannotModifyCard()
    {
        var (owner, _) = await AuthenticatedAsync();
        var (other, _) = await AuthenticatedAsync();
        var created = await owner.PostAsJsonAsync("/api/wishlist", new { url = "https://shop.example/p", name = "Keep" });
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        var forbidden = await other.PutAsJsonAsync("/api/wishlist/" + card.Id, new { url = card.Url, name = "Attack" });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        var hiddenDelete = await other.DeleteAsync("/api/wishlist/" + card.Id);
        Assert.Equal(HttpStatusCode.NoContent, hiddenDelete.StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<WishlistItemDto[]>("/api/wishlist"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/wishlist/" + card.Id)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<WishlistItemDto[]>("/api/wishlist"))!);
    }

    [Fact]
    public async Task Reserve_OwnerCannotSeeReservation_AndPrivateWishlistIsDenied()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (reserver, _) = await AuthenticatedAsync();
        var created = await owner.PostAsJsonAsync("/api/wishlist", new { url = "https://shop.example/p", name = "Surprise" });
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/wishlist/{card.Id}/reserve", null)).StatusCode);
        var response = await reserver.PostAsync($"/api/wishlist/{card.Id}/reserve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<WishlistItemDto>())!.IsReservedByMe);
        var ownerCard = Assert.Single((await owner.GetFromJsonAsync<WishlistItemDto[]>("/api/wishlist"))!);
        Assert.False(ownerCard.IsReserved);
        Assert.False(ownerCard.IsReservedByMe);
        Assert.Null(ownerCard.ReservedBy);
        Assert.Empty(await _store.Db.Notifications.ToListAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/wishlist/{card.Id}/cancel-reservation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reserver.PostAsync($"/api/wishlist/{card.Id}/cancel-reservation", null)).StatusCode);
        ownerUser.WishlistPrivacy = WishlistPrivacy.Private;
        await _store.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await reserver.GetAsync($"/api/wishlist/user/{ownerUser.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reserver.PostAsync($"/api/wishlist/{card.Id}/reserve", null)).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAuthentication_ReturnUnauthorized()
    {
        var client = _host.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/wishlist")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/wishlist",
            new { url = "https://shop.example/p", name = "Gift" })).StatusCode);
        Assert.Empty(await _store.Db.WishlistItems.ToListAsync());
    }

    [Fact]
    public async Task Refresh_RemovedRoute_ReturnsNotFound()
    {
        var (client, _) = await AuthenticatedAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/wishlist/{Guid.NewGuid()}/refresh", null)).StatusCode);
    }


    [Theory]
    [InlineData("name")]
    [InlineData("currency")]
    public async Task Save_NulInNameOrCurrency_ReturnsBadRequest(string field)
    {
        var (client, _) = await AuthenticatedAsync();
        var payload = new
        {
            url = "https://shop.example/nul",
            name = field == "name" ? "a\0b" : "Gift",
            currency = field == "currency" ? "R\0B" : "RUB"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/wishlist", payload)).StatusCode);
        var created = await client.PostAsJsonAsync("/api/wishlist", new { url = payload.url, name = "Keep" });
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/wishlist/" + card.Id, payload)).StatusCode);
        Assert.Equal("Keep", (await _store.Db.WishlistItems.AsNoTracking().SingleAsync()).Name);
    }
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
