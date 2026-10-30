using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WishHub.Api.Controllers;
using WishHub.Api.Extensions;
using WishHub.Core.DTOs;
using WishHub.Core.Interfaces;

namespace WishHub.Tests.Api;

public sealed class MetadataControllerTests
{
    [Fact]
    public async Task Resolve_AnonymousRequest_ReturnsUnauthorizedWithoutServiceCall()
    {
        var service = new Spy();
        using var server = Server(service);
        using var client = server.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = "https://ozon.ru/product/123/" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(service.Urls);
    }

    [Fact]
    public async Task Resolve_AuthorizedRequest_ReturnsFlatContractWithoutPriceOrPersistence()
    {
        var service = new Spy();
        using var server = Server(service);
        using var client = Authorized(server);
        using var response = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = " https://ozon.ru/product/123/?sku=123&variant=red " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "imageUrl", "name", "reason", "source", "status" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("partial", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("A title", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("imageUrl").ValueKind);
        Assert.Equal("ozon", json.RootElement.GetProperty("source").GetString());
        Assert.Equal("missing_fields", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal("https://ozon.ru/product/123/?sku=123&variant=red", Assert.Single(service.Urls));
        Assert.True(service.CancellationSupported);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("relative")]
    [InlineData("scheme")]
    [InlineData("credentials")]
    [InlineData("long")]
    public async Task Resolve_InvalidUrl_ReturnsBadRequestWithoutServiceCall(string scenario)
    {
        var url = scenario switch
        {
            "missing" => null,
            "blank" => "   ",
            "relative" => "/product/123",
            "scheme" => "file:///etc/passwd",
            "credentials" => "https://user:pass@ozon.ru/product/123/",
            _ => "https://ozon.ru/product/123/?x=" + new string('a', 2000)
        };
        var service = new Spy();
        using var server = Server(service);
        using var client = Authorized(server);
        using var response = await client.PostAsJsonAsync("/api/metadata/resolve", new { url });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Urls);
    }

    [Fact]
    public async Task Resolve_UrlAtLimitWithOuterWhitespace_ValidatesLengthAfterTrim()
    {
        const string prefix = "https://ozon.ru/product/123/?x=";
        var url = prefix + new string('a', 2000 - prefix.Length);
        var service = new Spy();
        using var server = Server(service);
        using var client = Authorized(server);
        using var response = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = "  " + url + "  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(url, Assert.Single(service.Urls));
        using var oversized = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = "  " + url + "a  " });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Single(service.Urls);
    }

    [Fact]
    public async Task Resolve_SeventhRequest_IsRateLimitedPerUserWithoutQueue()
    {
        var service = new Spy();
        using var server = Server(service);
        using var client = Authorized(server);
        for (var i = 0; i < 6; i++)
        {
            using var accepted = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = "https://ozon.ru/product/123/" });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var rejected = await client.PostAsJsonAsync("/api/metadata/resolve", new { url = "https://ozon.ru/product/123/" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(6, service.Urls.Count);
        using var anotherUser = Authorized(server, "second-user");
        using var independent = await anotherUser.PostAsJsonAsync("/api/metadata/resolve", new { url = "https://ozon.ru/product/123/" });
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        Assert.Equal(7, service.Urls.Count);
    }

    private static TestServer Server(Spy service) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
            services.AddAuthorization();
            services.AddProductMetadata(new ConfigurationBuilder().Build());
            services.RemoveAll<IProductMetadataService>();
            services.AddSingleton<IProductMetadataService>(service);
            services.AddControllers().AddApplicationPart(typeof(MetadataController).Assembly);
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));

    private static HttpClient Authorized(TestServer server, string user = "first-user")
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }
    private sealed class Spy : IProductMetadataService
    {
        public List<string> Urls { get; } = [];
        public bool CancellationSupported { get; private set; }
        public Task<ProductMetadataDto> ResolveAsync(string url, CancellationToken ct = default)
        {
            Urls.Add(url);
            CancellationSupported = ct.CanBeCanceled;
            return Task.FromResult(new ProductMetadataDto("partial", "A title", null, "ozon", "missing_fields"));
        }
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user) }, "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
