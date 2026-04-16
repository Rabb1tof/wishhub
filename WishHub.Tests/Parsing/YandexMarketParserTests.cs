using System.Net;
using Moq;
using Moq.Protected;
using WishHub.Core.Entities;
using WishHub.Parsing.Parsers;
using WishHub.Tests;
using Xunit;

namespace WishHub.Tests.Parsing;

public class YandexMarketParserTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;

    public YandexMarketParserTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Theory]
    [InlineData("https://market.yandex.ru/card/123", true)]
    [InlineData("https://market.yandex.com/card/456", true)]
    [InlineData("https://www.ozon.ru/product/123", false)]
    [InlineData("https://example.com/product", false)]
    public void CanParse_ReturnsCorrectResult(string url, bool expected)
    {
        var parser = new YandexMarketParser(_httpClient);
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
        </head>
        <body>
            <h1 data-zone-name='title'>Test Product</h1>
            <span data-auto='price-value'>2 499 ₽</span>
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

        var parser = new YandexMarketParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://market.yandex.ru/card/123");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test Product", result.Name);
        Assert.Equal(2499m, result.Price);
        Assert.Equal("RUB", result.Currency);
        Assert.Equal("https://example.com/image.jpg", result.ImageUrl);
        Assert.Equal(ProductSource.YandexMarket, result.Source);
    }

    [Fact]
    public async Task ParseAsync_WithMissingName_ReturnsFailure()
    {
        // Arrange
        var htmlResponse = @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta property='og:image' content='https://example.com/image.jpg' />
        </head>
        <body>
            <span data-auto='price-value'>2 499 ₽</span>
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

        var parser = new YandexMarketParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://market.yandex.ru/card/123");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Could not extract product name", result.ErrorMessage);
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

        var parser = new YandexMarketParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://market.yandex.ru/card/123");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("HTTP parsing failed", result.ErrorMessage);
    }

    [Trait("Category", "Integration")]
    [LiveFact]
    public async Task ParseAsync_RealUrl_ParsesSuccessfully()
    {
        // Arrange
        var httpClient = new HttpClient();
        var parser = new YandexMarketParser(httpClient);
        var url = "https://market.yandex.ru/card/myachi-dlya-nastolnogo-tennisa-donic-schildkrot-3-sht-belyy/4428256934";

        // Act
        var result = await parser.ParseAsync(url);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Name);
        Assert.Equal(ProductSource.YandexMarket, result.Source);
    }
}
