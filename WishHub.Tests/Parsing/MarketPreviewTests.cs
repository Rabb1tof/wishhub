using System.Net;
using WishHub.Core.DTOs;
using WishHub.Parsing;

namespace WishHub.Tests.Parsing;

public sealed class MarketPreviewTests
{
    private const string Card = "https://market.yandex.ru/card/besprovodnyye-naushniki-commo-etude-s-funktsiyey-aktivnogo-shumopodavleniya-anc-lavandovyy/5828126315";
    private const string Shop = "https://commo.yandex.ru/collections/besprovodnye-naushniki";
    private const string Name = "Наушники COMMO Etude с функцией адаптивного шумоподавления ANC, лавандовый";
    private const string Photo = "https://avatars.mds.yandex.net/get-yastore/18764786/lavender/orig";
    private const string Catalog = """
        <script data-rh="true" type="application/ld+json">{"@type":"OfferCatalog","itemListElement":[
          {"@type":"Offer","name":"Наушники COMMO Etude с функцией адаптивного шумоподавления ANC, графитовый",
           "image":"https://avatars.mds.yandex.net/get-yastore/18764786/graphite/orig"},
          {"@type":"Offer","name":"Наушники COMMO Etude с функцией адаптивного шумоподавления ANC, лавандовый",
           "image":"https://avatars.mds.yandex.net/get-yastore/18764786/lavender/orig"}
        ]}</script>
        """;

    [Theory]
    [InlineData("full")]
    [InlineData("market-short")]
    [InlineData("ya-cc")]
    public async Task ResolveAsync_MarketLink_FindsMatchingColorWithoutLoadingMarketCard(string format)
    {
        var shortUrl = format == "ya-cc" ? "https://ya.cc/m/AbC123" : "https://market.yandex.ru/cc/AbC123";
        var url = (format == "full" ? Card : shortUrl) + "?ref=example#ignored";
        using var handler = new CatalogHandler(uri => uri.AbsoluteUri == Shop ? Html(Catalog) : Redirect(Card + "?offerid=demo"));
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        var result = await service.ResolveAsync(url);

        Assert.Equal(new ProductMetadataDto("ok", Name, Photo, "yandexMarket", null), result);
        Assert.Equal(format == "full" ? new[] { Shop } : new[] { shortUrl, Shop }, handler.Requests);
    }

    [Theory]
    [InlineData("no-color")]
    [InlineData("ambiguous-color")]
    [InlineData("plus-model")]
    [InlineData("unknown-family")]
    [InlineData("vacuum-accessory")]
    [InlineData("headphone-accessory")]
    public async Task ResolveAsync_MarketUnqualifiedVariant_LeavesManualEntryWhenNoVerifiedRegionalCard(string variant)
    {
        var slug = variant switch
        {
            "no-color" => "robot-pylesos-dreame-f21-rlf42ga",
            "ambiguous-color" => "robot-pylesos-dreame-f21-black-white",
            "plus-model" => "robot-pylesos-dreame-f21-plus-black",
            "vacuum-accessory" => "filtr-dlya-dreame-f21-black",
            "headphone-accessory" => "chekhol-dlya-naushniki-commo-etude-s-lavandovyy",
            _ => "robot-pylesos-other-f21-black"
        };
        using var handler = new CatalogHandler(_ => Html("<html>Empty public catalog</html>"));
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        var result = await service.ResolveAsync($"https://market.yandex.ru/card/{slug}/1234567890");

        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "yandexMarket", "missing_fields"), result);
        Assert.Equal(new[] { $"https://market.yandex.uz/ru/card/{slug}/1234567890" }, handler.Requests);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("non-https")]
    [InlineData("credentials")]
    [InlineData("another-shortcut")]
    public async Task ResolveAsync_MarketShortRedirect_RejectsUntrustedTarget(string target)
    {
        var destination = target switch
        {
            "foreign" => Card.Replace("market.yandex.ru", "market.yandex.ru.example.test"),
            "non-https" => Card.Replace("https:", "http:"),
            "credentials" => Card.Replace("https://", "https://credential@"),
            _ => "https://market.yandex.ru/cc/OtherCode"
        };
        using var handler = new CatalogHandler(_ => Redirect(destination));
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        var result = await service.ResolveAsync("https://market.yandex.ru/cc/AbC123");

        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "yandexMarket", "redirect_rejected"), result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResolveAsync_MarketDisabled_DoesNotSendShortLink()
    {
        using var handler = new CatalogHandler(_ => throw new InvalidOperationException("unexpected request"));
        using var service = new ProductMetadataService(new HttpClient(handler));

        Assert.Equal("source_disabled", (await service.ResolveAsync("https://market.yandex.ru/cc/AbC123")).Reason);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("invalid-image-host")]
    [InlineData("different-variant")]
    public async Task ResolveAsync_MarketCommoCatalog_RejectsWrongImageAuthorityOrVariant(string scenario)
    {
        var catalog = scenario == "invalid-image-host" ? Catalog.Replace(Photo, Photo.Replace("avatars.mds.yandex.net", "other.example"))
            : Catalog.Replace("ANC, лавандовый", "ANC, графитовый");
        using var handler = new CatalogHandler(_ => Html(catalog));
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        Assert.Equal(new ProductMetadataDto("unavailable", null, null, "yandexMarket", "missing_fields"), await service.ResolveAsync(Card));
        Assert.Equal(new[] { Shop, Card.Replace("market.yandex.ru/", "market.yandex.uz/ru/") }, handler.Requests);
    }

    [Theory]
    [InlineData("short", true)]
    [InlineData("full", true)]
    [InlineData("wrong-share-id", false)]
    [InlineData("wrong-compose-id", false)]
    [InlineData("foreign-image", false)]
    public async Task ResolveAsync_MarketRegionalCard_UsesOnlySameArticleShareData(string format, bool accepted)
    {
        const string path = "/card/termobelye-chernyy-xl/4726842624";
        const string shortUrl = "https://market.yandex.ru/cc/BZFuwr";
        const string image = "https://avatars.mds.yandex.net/get-mpic/19826024/2a000001photo/orig";
        var shareId = format == "wrong-share-id" ? "4726842623" : "4726842624";
        var composeId = format == "wrong-compose-id" ? "4726842623" : "4726842624";
        var picture = format == "foreign-image" ? image.Replace("avatars.mds.yandex.net", "other.example") : image;
        var regional = $$$$"""
            <html><noframes data-apiary="patch">{
                "widgets":{"@card/ShortUrl":{}},
                "collections":{
                    "share":{"offer":{"context":{"osku":"{{{{shareId}}}}","type":"ITEM"},"title":"Комплект термобелья","picture":"{{{{picture}}}}"}},
                    "compose":{"offer":{"oskuId":"{{{{composeId}}}}"}}
                }
            }</noframes></html>
            """;
        using var handler = new CatalogHandler(uri => uri.AbsoluteUri == shortUrl
            ? Redirect("https://market.yandex.ru" + path + "?offerid=redirected") : Html(regional));
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);
        var url = format == "short" ? shortUrl : "https://market.yandex.ru" + path + "?offerid=pasted";

        var result = await service.ResolveAsync(url);

        Assert.Equal(accepted ? new ProductMetadataDto("ok", "Комплект термобелья", image, "yandexMarket", null)
            : new ProductMetadataDto("unavailable", null, null, "yandexMarket", "missing_fields"), result);
        var expected = "https://market.yandex.uz/ru" + path;
        Assert.Equal(format == "short" ? new[] { shortUrl, expected } : new[] { expected }, handler.Requests);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("rate-limited")]
    [InlineData("invalid-json")]
    [InlineData("network-error")]
    [InlineData("connection-timeout")]
    public async Task ResolveAsync_MarketCatalogFailure_UsesVerifiedRegionalCard(string failure)
    {
        const string image = "https://avatars.mds.yandex.net/get-mpic/19826024/regional/orig";
        const string regional = """
            <noframes data-apiary="patch">{
                "widgets":{"@card/ShortUrl":{}},
                "collections":{
                    "share":{"offer":{"context":{"osku":"5828126315","type":"ITEM"},"title":"Regional COMMO Etude","picture":"https://avatars.mds.yandex.net/get-mpic/19826024/regional/orig"}},
                    "compose":{"offer":{"oskuId":"5828126315"}}
                }
            }</noframes>
            """;
        var regionalUrl = Card.Replace("market.yandex.ru/", "market.yandex.uz/ru/");
        using var handler = new CatalogHandler(uri =>
        {
            if (uri.AbsoluteUri == regionalUrl) return Html(regional);
            Assert.Equal(Shop, uri.AbsoluteUri);
            return failure switch
            {
                "forbidden" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                "rate-limited" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                "invalid-json" => Html("<script type=\"application/ld+json\">{broken}</script>"),
                "network-error" => throw new HttpRequestException("No response"),
                _ => throw new TaskCanceledException("Connection timeout", new TimeoutException())
            };
        });
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        var result = await service.ResolveAsync(Card);

        Assert.Equal(new ProductMetadataDto("ok", "Regional COMMO Etude", image, "yandexMarket", null), result);
        Assert.Equal(new[] { Shop, regionalUrl }, handler.Requests);
    }

    [Fact]
    public async Task ResolveAsync_MarketCatalogCancelled_DoesNotStartRegionalLookup()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new CatalogHandler(uri =>
        {
            Assert.Equal(Shop, uri.AbsoluteUri);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var service = new ProductMetadataService(new HttpClient(handler), enableMarketPreview: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveAsync(Card, cancellation.Token));
        Assert.Equal(new[] { Shop }, handler.Requests);
    }

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage Redirect(string url)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(url);
        return response;
    }

    private sealed class CatalogHandler(Func<Uri, HttpResponseMessage> get) : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(get(request.RequestUri));
        }
    }
}
