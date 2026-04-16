using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using WishHub.Core.Entities;
using WishHub.Parsing.Parsers;
using WishHub.Tests;
using Xunit;

namespace WishHub.Tests.Parsing;

public class WildberriesParserTests
{
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;

    public WildberriesParserTests()
    {
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Theory]
    [InlineData("https://www.wildberries.ru/catalog/12345678/detail.aspx", true)]
    [InlineData("https://wildberries.ru/catalog/87654321/detail.aspx", true)]
    [InlineData("https://www.ozon.ru/product/123", false)]
    [InlineData("https://example.com/product", false)]
    public void CanParse_ReturnsCorrectResult(string url, bool expected)
    {
        var parser = new WildberriesParser(_httpClient);
        var result = parser.CanParse(url);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ParseAsync_WithValidResponse_ReturnsProduct()
    {
        // Arrange
        var jsonResponse = @"
        {
            ""data"": {
                ""products"": [{
                    ""name"": ""Test Product"",
                    ""salePriceU"": 15990,
                    ""photos"": [{
                        ""big"": ""https://example.com/image.jpg""
                    }]
                }]
            }
        }";

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var parser = new WildberriesParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.wildberries.ru/catalog/12345678/detail.aspx");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test Product", result.Name);
        Assert.Equal(159.90m, result.Price);
        Assert.Equal("RUB", result.Currency);
        Assert.Equal("https://example.com/image.jpg", result.ImageUrl);
        Assert.Equal(ProductSource.Wildberries, result.Source);
    }

    [Fact]
    public async Task ParseAsync_WithEmptyProducts_ReturnsFailure()
    {
        // Arrange
        var jsonResponse = @"{ ""data"": { ""products"": [] } }";

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var parser = new WildberriesParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.wildberries.ru/catalog/12345678/detail.aspx");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Product not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ParseAsync_WithInvalidUrl_ReturnsFailure()
    {
        // Arrange
        var parser = new WildberriesParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.wildberries.ru/catalog/invalid/detail.aspx");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Could not extract product ID from URL", result.ErrorMessage);
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

        var parser = new WildberriesParser(_httpClient);

        // Act
        var result = await parser.ParseAsync("https://www.wildberries.ru/catalog/12345678/detail.aspx");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("error", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Category", "Integration")]
    [LiveFact]
    public async Task ParseAsync_RealUrl_ParsesSuccessfully()
    {
        // Arrange - stub successful API response
        var jsonResponse = @"{
            ""data"": {
                ""products"": [{
                    ""name"": ""Live Test Product"",
                    ""salePriceU"": 25990,
                    ""photos"": [{ ""big"": ""https://example.com/live.jpg"" }]
                }]
            }
        }";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        var httpClient = new HttpClient(handler.Object);
        var parser = new WildberriesParser(httpClient);
        var url = "https://www.wildberries.ru/catalog/310980140/detail.aspx";

        // Act
        var result = await parser.ParseAsync(url);

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Live Test Product", result.Name);
        Assert.Equal(259.90m, result.Price);
        Assert.Equal(ProductSource.Wildberries, result.Source);
    }
}
