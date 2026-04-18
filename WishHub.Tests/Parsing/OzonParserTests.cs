using System.Net;
using HtmlAgilityPack;
using Moq;
using Moq.Protected;
using WishHub.Core.Entities;
using WishHub.Parsing.Ozon;
using WishHub.Parsing.Parsers;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Tests;
using Xunit;

namespace WishHub.Tests.Parsing;

public class OzonParserTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;

    public OzonParserTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Theory]
    [InlineData("https://www.ozon.ru/product/123", true)]
    [InlineData("https://ozon.ru/product/456", true)]
    [InlineData("https://www.ozon.com/product/789", true)]
    [InlineData("https://www.wildberries.ru/catalog/123", false)]
    [InlineData("https://example.com/product", false)]
    public void CanParse_ReturnsCorrectResult(string url, bool expected)
    {
        var parser = new OzonParser(_httpClient);
        var result = parser.CanParse(url);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ParseAsync_WithValidHtml_ReturnsProduct()
    {
        // Arrange
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:title' content='Test Product' />
            <meta property='og:image' content='https://example.com/image.jpg' />
            <meta property='product:price:amount' content='1599' />
        </head>
        <body>
            <h1>Test Product</h1>
            <span class='c3021-a1'>1 599 ₽</span>
        </body>
        </html>";

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlResponse)
            });

        var parser = new OzonParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.ozon.ru/product/123");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test Product", result.Name);
        Assert.Equal(1599m, result.Price);
        Assert.Equal("RUB", result.Currency);
        Assert.Equal("https://example.com/image.jpg", result.ImageUrl);
        Assert.Equal(ProductSource.Ozon, result.Source);
    }

    [Fact]
    public async Task ParseAsync_WithMissingPrice_ReturnsFailure()
    {
        // Arrange
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:title' content='Test Product' />
        </head>
        <body>
            <h1>Test Product</h1>
        </body>
        </html>";

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlResponse)
            });

        var parser = new OzonParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.ozon.ru/product/123");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Could not extract all required fields", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_WithHttpError_ReturnsFailure()
    {
        // Arrange
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        var parser = new OzonParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.ozon.ru/product/123");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("HTTP parsing failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_WithStealthScraper_ReturnsMappedProductWithoutHttpFallback()
    {
        var scraper = new Mock<IOzonProductScraper>();
        scraper
            .Setup(x => x.ScrapeProductAsync("https://www.ozon.ru/product/123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OzonProduct
            {
                Url = "https://www.ozon.ru/product/123",
                Title = "Stealth Product",
                Price = 2499m,
                Rating = 4.7m,
                ReviewCount = 18,
                Description = "desc",
                Images = ["https://example.com/stealth.jpg"]
            });

        var parser = new OzonParser(_httpClient, scraper.Object, new FingerprintPool());

        var result = await parser.ParseAsync("https://www.ozon.ru/product/123");

        Assert.True(result.Success);
        Assert.Equal("Stealth Product", result.Name);
        Assert.Equal(2499m, result.Price);
        Assert.Equal("https://example.com/stealth.jpg", result.ImageUrl);
        Assert.Equal(ProductSource.Ozon, result.Source);
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Trait("Category", "Integration")]
    [LiveFact]
    public async Task ParseAsync_RealUrl_ParsesSuccessfully()
    {
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:title' content='Live Ozon Product' />
            <meta property='og:image' content='https://example.com/ozon.jpg' />
            <meta property='product:price:amount' content='3199' />
        </head>
        <body>
            <h1>Live Ozon Product</h1>
            <div data-widget='webPrice'>3 199 ₽</div>
        </body>
        </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlResponse)
            });

        var httpClient = new HttpClient(handler.Object);
        var parser = new OzonParser(httpClient);
        var url = "https://www.ozon.ru/product/raketka-dlya-nastolnogo-tennisa-boshika-championship-2-zvezdy-270783132/";

        // Act
        var result = await parser.ParseAsync(url);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Live Ozon Product", result.Name);
        Assert.Equal(3199m, result.Price);
        Assert.Equal(ProductSource.Ozon, result.Source);
    }
}
