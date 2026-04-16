using Moq;
using Moq.Protected;
using WishHub.Parsing.Parsers;
using WishHub.Tests;
using Xunit;

namespace WishHub.Tests.Parsing;

public class ParserTests
{
    private readonly HttpClient _httpClient;

    public ParserTests()
    {
        _httpClient = new HttpClient();
    }

    [Theory]
    [InlineData("https://www.wildberries.ru/catalog/310980140/detail.aspx", true)]
    [InlineData("https://www.ozon.ru/product/raketka-dlya-nastolnogo-tennisa-boshika-championship-2-zvezdy-270783132/", true)]
    [InlineData("https://market.yandex.ru/card/myachi-dlya-nastolnogo-tennisa-donic-schildkrot-3-sht-belyy/4428256934", true)]
    [InlineData("https://example.com/product/123", false)]
    public void CanParse_ReturnsCorrectResult(string url, bool expected)
    {
        var wbParser = new WildberriesParser(_httpClient);
        var ozonParser = new OzonParser(_httpClient);
        var ymParser = new YandexMarketParser(_httpClient);

        var canParse = wbParser.CanParse(url) || ozonParser.CanParse(url) || ymParser.CanParse(url);

        Assert.Equal(expected, canParse);
    }

    [LiveFact]
    public async Task WildberriesParser_ParsesRealProduct()
    {
        var jsonResponse = @"{
            ""data"": {
                ""products"": [{
                    ""name"": ""Live WB Product"",
                    ""salePriceU"": 19990,
                    ""photos"": [{ ""big"": ""https://example.com/live-wb.jpg"" }]
                }]
            }
        }";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var client = new HttpClient(handler.Object);
        var parser = new WildberriesParser(client);
        var url = "https://www.wildberries.ru/catalog/310980140/detail.aspx";

        var result = await parser.ParseAsync(url);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Live WB Product", result.Name);
        Assert.Equal(199.90m, result.Price);
        Assert.Equal(Core.Entities.ProductSource.Wildberries, result.Source);
    }

    [LiveFact]
    public async Task OzonParser_ParsesRealProduct()
    {
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:title' content='Live Ozon Product' />
            <meta property='og:image' content='https://example.com/ozon-live.jpg' />
            <meta property='product:price:amount' content='2799' />
        </head>
        <body>
            <h1>Live Ozon Product</h1>
            <div data-widget='webPrice'>2 799 ₽</div>
        </body>
        </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlResponse)
            });

        var client = new HttpClient(handler.Object);
        var parser = new OzonParser(client);
        var url = "https://www.ozon.ru/product/raketka-dlya-nastolnogo-tennisa-boshika-championship-2-zvezdy-270783132/";

        var result = await parser.ParseAsync(url);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Name);
        Assert.Equal(2799m, result.Price);
    }

    [LiveFact]
    public async Task YandexMarketParser_ParsesRealProduct()
    {
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:title' content='Live YM Product' />
            <meta property='og:image' content='https://example.com/ym-live.jpg' />
        </head>
        <body>
            <h1 data-zone-name='title'>Live YM Product</h1>
            <span data-auto='price-value'>1 499 ₽</span>
        </body>
        </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlResponse)
            });

        var client = new HttpClient(handler.Object);
        var parser = new YandexMarketParser(client);
        var url = "https://market.yandex.ru/card/myachi-dlya-nastolnogo-tennisa-donic-schildkrot-3-sht-belyy/4428256934";

        var result = await parser.ParseAsync(url);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Name);
        Assert.Equal(1499m, result.Price);
    }
}
