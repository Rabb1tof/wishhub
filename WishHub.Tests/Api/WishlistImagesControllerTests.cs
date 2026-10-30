using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using WishHub.Core.Validation;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Tests.Infrastructure;

namespace WishHub.Tests.Api;

public sealed class WishlistImagesControllerTests : IAsyncLifetime
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII=");
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
                services.AddScoped<IWishlistImageService, WishlistImageService>();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(WishlistImagesController).Assembly);
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

    [Fact]
    public async Task Upload_SaveAndClear_ControlsDraftAndPublicAccessWithoutChangingCardFields()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (other, _) = await AuthenticatedAsync();
        using var anonymous = _host.GetTestClient();
        var imageUrl = await UploadImageAsync(owner, Png, "image/png", "gift.png");

        Assert.True(ManagedWishlistImage.TryParse(imageUrl, out var image));
        Assert.Equal(ownerUser.Id, image.OwnerId);
        Assert.Equal("png", image.Extension);
        await AssertImageAsync(owner, imageUrl, Png, "image/png");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(imageUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(imageUrl)).StatusCode);

        var savedResponse = await owner.PostAsJsonAsync("/api/wishlist", new
        {
            url = "https://www.ozon.ru/product/gift?sku=2", name = "My own name",
            imageUrl, price = 12.345m, currency = "USD", source = "Unknown"
        });
        Assert.Equal(HttpStatusCode.Created, savedResponse.StatusCode);
        var card = (await savedResponse.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal("My own name", card.Name);
        Assert.Equal("https://www.ozon.ru/product/gift?sku=2", card.Url);
        Assert.Equal(12.345m, card.Price);
        Assert.Equal("USD", card.Currency);
        Assert.Equal("Ozon", card.Source);
        Assert.Equal(imageUrl, card.ImageUrl);
        await AssertImageAsync(anonymous, imageUrl, Png, "image/png");
        await AssertImageAsync(other, imageUrl, Png, "image/png");
        Assert.Empty(await _store.Db.Products.ToListAsync());

        var clearedResponse = await owner.PutAsJsonAsync("/api/wishlist/" + card.Id, new
        {
            url = card.Url, name = card.Name, imageUrl = (string?)null, price = card.Price, currency = card.Currency
        });
        Assert.Equal(HttpStatusCode.OK, clearedResponse.StatusCode);
        var cleared = (await clearedResponse.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Null(cleared.ImageUrl);
        Assert.Equal(card.Source, cleared.Source);
        Assert.Equal(card.Price, cleared.Price);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(imageUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(imageUrl)).StatusCode);
        await AssertImageAsync(owner, imageUrl, Png, "image/png");
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("webp")]
    public async Task Upload_AcceptedFormats_ReturnsCanonicalExtensionAndDetectedMime(string format)
    {
        var (owner, _) = await AuthenticatedAsync();
        var bytes = format == "jpg"
            ? Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=")
            : Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");
        var mime = format == "jpg" ? "image/jpeg" : "image/webp";
        var imageUrl = await UploadImageAsync(owner, bytes, mime, "gift." + format);

        Assert.EndsWith("." + format, imageUrl, StringComparison.Ordinal);
        await AssertImageAsync(owner, imageUrl, bytes, mime);
    }

    [Fact]
    public async Task Upload_AuthenticationAndQueryOwner_UsesAuthenticatedIdentityOnly()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (other, otherUser) = await AuthenticatedAsync();
        using var anonymous = _host.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await UploadAsync(anonymous, new ByteArrayContent(Png), "image/png", "gift.png")).StatusCode);
        var imageUrl = await UploadImageAsync(owner, Png, "image/png", "gift.png",
            "/api/wishlist-images?ownerId=" + otherUser.Id);
        Assert.StartsWith($"/api/wishlist-images/{ownerUser.Id:D}/", imageUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync(imageUrl + "?viewerId=" + ownerUser.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync(imageUrl + "?ownerId=" + ownerUser.Id)).StatusCode);
        await AssertImageAsync(owner, imageUrl, Png, "image/png");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("svg")]
    [InlineData("fake-png")]
    [InlineData("truncated-png")]
    [InlineData("mismatched-mime")]
    public async Task Upload_InvalidContent_RejectsWithoutCreatingFileOrCard(string invalidCase)
    {
        var (owner, _) = await AuthenticatedAsync();
        var bytes = invalidCase switch
        {
            "empty" => Array.Empty<byte>(),
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
            "fake-png" => Encoding.UTF8.GetBytes("<html>This is not an image.</html>"),
            "truncated-png" => Png[..8],
            _ => Png
        };
        var mime = invalidCase == "svg" ? "image/svg+xml" :
            invalidCase == "mismatched-mime" ? "image/jpeg" : "image/png";

        var response = await UploadAsync(owner, new ByteArrayContent(bytes), mime, "gift.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _store.Db.WishlistItems.ToListAsync());
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Upload_ExactlyMaxFileBytes_AcceptsAndReturnsAllBytes()
    {
        var (owner, _) = await AuthenticatedAsync();
        var bytes = new byte[checked((int)WishlistImageLimits.MaxFileBytes)];
        Png.CopyTo(bytes, 0);

        var imageUrl = await UploadImageAsync(owner, bytes, "image/png", "large.png");

        await AssertImageAsync(owner, imageUrl, bytes, "image/png");
        Assert.Equal(WishlistImageLimits.MaxFileBytes, new FileInfo(Assert.Single(StoredFiles())).Length);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(65536)]
    public async Task Upload_OverSizeWithoutDeclaredLength_Returns413AndLeavesNoFile(int excessBytes)
    {
        var (owner, _) = await AuthenticatedAsync();
        var bytes = new byte[checked((int)WishlistImageLimits.MaxFileBytes + excessBytes)];
        Png.CopyTo(bytes, 0);

        var response = await UploadAsync(owner, new UnmeasuredContent(bytes), "image/png", "large.png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(StoredFiles());
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("duplicate-file")]
    [InlineData("extra-field")]
    public async Task Upload_InvalidMultipart_RejectsWholeRequestWithoutFinalizedFile(string invalidCase)
    {
        var (owner, _) = await AuthenticatedAsync();
        using var form = new MultipartFormDataContent();
        if (invalidCase != "missing-file")
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "gift.png");
        }
        if (invalidCase == "duplicate-file")
        {
            var second = new ByteArrayContent(Png);
            second.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(second, "file", "another.png");
        }
        if (invalidCase == "extra-field")
            form.Add(new StringContent("spoofed"), "ownerId");

        var response = await owner.PostAsync("/api/wishlist-images", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Upload_OwnerQuotaReached_Returns409AndPreservesExistingFiles()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var directory = Path.Combine(_store.ImageRootPath, ownerUser.Id.ToString("D"));
        Directory.CreateDirectory(directory);
        var existingPath = Path.Combine(directory, Guid.NewGuid().ToString("D") + ".png");
        await using (var existing = File.Create(existingPath))
            existing.SetLength(WishlistImageLimits.MaxOwnerBytes);
        File.SetLastWriteTimeUtc(existingPath, DateTime.UtcNow);

        var response = await UploadAsync(owner, new ByteArrayContent(Png), "image/png", "gift.png");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(existingPath, Assert.Single(StoredFiles()));
        Assert.Equal(WishlistImageLimits.MaxOwnerBytes, new FileInfo(existingPath).Length);
        Assert.Empty(await _store.Db.WishlistItems.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_PrivateAndFriendsOnly_RequiresCurrentPrivacyEvenForReserver(bool reverseFriendship)
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (viewer, viewerUser) = await AuthenticatedAsync();
        using var anonymous = _host.GetTestClient();
        var imageUrl = await UploadImageAsync(owner, Png, "image/png", "gift.png");
        var created = await owner.PostAsJsonAsync("/api/wishlist",
            new { url = "https://shop.example/gift", name = "Gift", imageUrl });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync($"/api/wishlist/{card.Id}/reserve", null)).StatusCode);
        await AssertImageAsync(anonymous, imageUrl, Png, "image/png");

        ownerUser.WishlistPrivacy = WishlistPrivacy.Private;
        await _store.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(imageUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(imageUrl)).StatusCode);
        await AssertImageAsync(owner, imageUrl, Png, "image/png");

        ownerUser.WishlistPrivacy = WishlistPrivacy.FriendsOnly;
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = reverseFriendship ? ownerUser.Id : viewerUser.Id,
            AddresseeId = reverseFriendship ? viewerUser.Id : ownerUser.Id,
            Status = FriendshipStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        _store.Db.Friendships.Add(friendship);
        await _store.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(imageUrl)).StatusCode);
        friendship.Status = FriendshipStatus.Accepted;
        await _store.Db.SaveChangesAsync();
        await AssertImageAsync(viewer, imageUrl, Png, "image/png");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(imageUrl)).StatusCode);
        friendship.Status = FriendshipStatus.Blocked;
        await _store.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(imageUrl)).StatusCode);
    }

    [Fact]
    public async Task Save_ForeignMissingOrNonCanonicalImageUrl_Returns400WithoutChangingCard()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (other, _) = await AuthenticatedAsync();
        var foreignImage = await UploadImageAsync(other, Png, "image/png", "other.png");
        var ownedImage = await UploadImageAsync(owner, Png, "image/png", "mine.png");
        var missingImage = $"/api/wishlist-images/{ownerUser.Id:D}/{Guid.NewGuid():D}.png";
        var created = await owner.PostAsJsonAsync("/api/wishlist",
            new { url = "https://shop.example/keep", name = "Keep", imageUrl = ownedImage });
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;

        foreach (var invalid in new[] { foreignImage, missingImage, ownedImage + "?ownerId=" + ownerUser.Id, ownedImage + "#fragment" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/wishlist",
                new { url = "https://shop.example/other", name = "Invalid", imageUrl = invalid })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/wishlist/" + card.Id,
                new { url = card.Url, name = "Invalid edit", imageUrl = invalid })).StatusCode);
        }
        var saved = await _store.Db.WishlistItems.AsNoTracking().SingleAsync();
        Assert.Equal(card.Id, saved.Id);
        Assert.Equal("Keep", saved.Name);
        Assert.Equal(ownedImage, saved.ImageUrl);
    }

    [Theory]
    [InlineData("plain-owned")]
    [InlineData("plain-foreign")]
    [InlineData("missing")]
    [InlineData("query")]
    [InlineData("encoded-prefix")]
    [InlineData("encoded-slash")]
    [InlineData("double-encoded-prefix")]
    [InlineData("dot-segments")]
    [InlineData("encoded-dot-segments")]
    [InlineData("double-encoded-dot-segments")]
    [InlineData("uppercase-namespace")]
    [InlineData("encoded-backslash")]
    public async Task Save_AbsoluteManagedImageNamespace_RejectsAddAndUpdateWithoutChangingCard(string scenario)
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var (other, _) = await AuthenticatedAsync();
        var ownedImage = await UploadImageAsync(owner, Png, "image/png", "mine.png");
        var foreignImage = await UploadImageAsync(other, Png, "image/png", "foreign.png");
        var missingImage = $"/api/wishlist-images/{ownerUser.Id:D}/{Guid.NewGuid():D}.png";
        const string origin = "https://images.example";
        var invalid = scenario switch
        {
            "plain-owned" => origin + ownedImage,
            "plain-foreign" => origin + foreignImage,
            "missing" => origin + missingImage,
            "query" => origin + foreignImage + "?cachebust=1",
            "encoded-prefix" => origin + foreignImage.Replace("/api/", "/%61pi/", StringComparison.Ordinal),
            "encoded-slash" => origin + "/" + foreignImage[1..].Replace("/", "%2F", StringComparison.Ordinal),
            "double-encoded-prefix" => origin + foreignImage.Replace("/api/", "/%2561pi/", StringComparison.Ordinal),
            "dot-segments" => origin + "/assets/.." + foreignImage,
            "encoded-dot-segments" => origin + "/assets/%2e%2e" + foreignImage,
            "double-encoded-dot-segments" => origin + "/assets/%252e%252e" + foreignImage,
            "uppercase-namespace" => origin + foreignImage.Replace("/api/wishlist-images/", "/API/WISHLIST-IMAGES/", StringComparison.Ordinal),
            "encoded-backslash" => origin + "/" + foreignImage[1..].Replace("/", "%5C", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var created = await owner.PostAsJsonAsync("/api/wishlist", new
        {
            url = "https://shop.example/keep", name = "Keep", imageUrl = ownedImage, price = 15m, currency = "USD"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;

        var rejectedAdd = await owner.PostAsJsonAsync("/api/wishlist",
            new { url = "https://shop.example/invalid", name = "Invalid add", imageUrl = invalid });
        var rejectedUpdate = await owner.PutAsJsonAsync("/api/wishlist/" + card.Id,
            new { url = card.Url, name = "Invalid update", imageUrl = invalid, price = 99m });

        Assert.Equal(HttpStatusCode.BadRequest, rejectedAdd.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdate.StatusCode);
        var saved = await _store.Db.WishlistItems.AsNoTracking().SingleAsync();
        Assert.Equal(card.Id, saved.Id);
        Assert.Equal("Keep", saved.Name);
        Assert.Equal(card.Url, saved.Url);
        Assert.Equal(ownedImage, saved.ImageUrl);
        Assert.Equal(15m, saved.Price);
        Assert.Equal("USD", saved.Currency);
        Assert.Equal(card.SortOrder, saved.SortOrder);
        await AssertImageAsync(owner, ownedImage, Png, "image/png");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task Save_ExternalImageUrlWithManagedLookingQuery_RemainsSupported(string scheme)
    {
        var (owner, _) = await AuthenticatedAsync();
        var imageUrl = scheme + "://cdn.example/photos/gift.png?next=%2Fapi%2Fwishlist-images%2Fpreview&size=2";
        var created = await owner.PostAsJsonAsync("/api/wishlist",
            new { url = "https://shop.example/external", name = "External", imageUrl });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<WishlistItemDto>())!;
        Assert.Equal(imageUrl, card.ImageUrl);
        var replacement = imageUrl.Replace("/photos/gift.png", "/photos/replacement.png", StringComparison.Ordinal);
        var updated = await owner.PutAsJsonAsync("/api/wishlist/" + card.Id,
            new { url = card.Url, name = "Updated external", imageUrl = replacement });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(replacement, (await updated.Content.ReadFromJsonAsync<WishlistItemDto>())!.ImageUrl);
        var saved = await _store.Db.WishlistItems.AsNoTracking().SingleAsync();
        Assert.Equal("Updated external", saved.Name);
        Assert.Equal(replacement, saved.ImageUrl);
        Assert.Empty(StoredFiles());
    }
    [Fact]
    public async Task Get_MalformedOrTraversalPath_Returns404WithoutServingManagedFile()
    {
        var (owner, ownerUser) = await AuthenticatedAsync();
        var imageUrl = await UploadImageAsync(owner, Png, "image/png", "gift.png");
        var filename = imageUrl[(imageUrl.LastIndexOf('/') + 1)..];
        var paths = new[]
        {
            "/api/wishlist-images/not-a-guid/" + filename,
            $"/api/wishlist-images/{ownerUser.Id:D}/not-a-guid.png",
            imageUrl.Replace(".png", ".svg", StringComparison.Ordinal),
            $"/api/wishlist-images/{ownerUser.Id:D}/%2e%2e%2f{filename}",
            $"/api/wishlist-images/{ownerUser.Id:D}/%2e%2e/{filename}",
            $"/api/wishlist-images/{Guid.Empty:D}/{filename}"
        };
        foreach (var path in paths)
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
        await AssertImageAsync(owner, imageUrl, Png, "image/png");
    }

    private async Task<(HttpClient Client, User User)> AuthenticatedAsync(WishlistPrivacy privacy = WishlistPrivacy.Public)
    {
        var user = await _store.AddUserAsync(privacy);
        var client = _host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.Id.ToString());
        return (client, user);
    }

    private static async Task<string> UploadImageAsync(HttpClient client, byte[] bytes, string mime, string filename,
        string route = "/api/wishlist-images")
    {
        using var response = await UploadAsync(client, new ByteArrayContent(bytes), mime, filename, route);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "imageUrl" }, json.EnumerateObject().Select(property => property.Name));
        return json.GetProperty("imageUrl").GetString()!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, HttpContent file, string mime,
        string filename, string route = "/api/wishlist-images")
    {
        using var form = new MultipartFormDataContent();
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", filename);
        return await client.PostAsync(route, form);
    }

    private static async Task AssertImageAsync(HttpClient client, string imageUrl, byte[] expected, string mime)
    {
        using var response = await client.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mime, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    private string[] StoredFiles() => Directory.Exists(_store.ImageRootPath)
        ? Directory.GetFiles(_store.ImageRootPath, "*", SearchOption.AllDirectories)
        : [];

    private sealed class UnmeasuredContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
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
