using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using WishHub.Parsing.Ozon;

namespace WishHub.Tests.Parsing.Ozon;

public class BlockDetectorTests
{
    [Fact]
    public async Task EnsureNotBlockedAsync_ThrowsWhenUrlContainsCaptchaMarker()
    {
        var page = new Mock<IPage>();
        page.SetupGet(x => x.Url).Returns("https://www.ozon.ru/captcha?foo=bar");

        var detector = new BlockDetector(NullLogger<BlockDetector>.Instance);

        var act = () => detector.EnsureNotBlockedAsync(page.Object, response: null, new Uri("https://www.ozon.ru/product/123"));

        await act.Should().ThrowAsync<OzonBlockedException>()
            .WithMessage("*URL marker*");
    }

    [Fact]
    public async Task EnsureNotBlockedAsync_ThrowsWhenRedirectedToUnexpectedDomain()
    {
        var page = new Mock<IPage>();
        page.SetupGet(x => x.Url).Returns("https://example.com/challenge");

        var detector = new BlockDetector(NullLogger<BlockDetector>.Instance);

        var act = () => detector.EnsureNotBlockedAsync(page.Object, response: null, new Uri("https://www.ozon.ru/product/123"));

        await act.Should().ThrowAsync<OzonBlockedException>()
            .WithMessage("*unexpected domain*");
    }

    [Fact]
    public async Task EnsureNotBlockedAsync_ThrowsWhenStatusCodeIsNot200()
    {
        var page = new Mock<IPage>();
        var response = new Mock<IResponse>();

        page.SetupGet(x => x.Url).Returns("https://www.ozon.ru/product/123");
        response.SetupGet(x => x.Status).Returns(403);

        var detector = new BlockDetector(NullLogger<BlockDetector>.Instance);

        var act = () => detector.EnsureNotBlockedAsync(page.Object, response.Object, new Uri("https://www.ozon.ru/product/123"));

        await act.Should().ThrowAsync<OzonBlockedException>()
            .Where(ex => ex.StatusCode == 403);
    }

    [Fact]
    public async Task EnsureNotBlockedAsync_ThrowsWhenHtmlContainsBlockedMarkers()
    {
        var page = new Mock<IPage>();

        page.SetupGet(x => x.Url).Returns("https://www.ozon.ru/product/123");
        page.Setup(x => x.ContentAsync()).ReturnsAsync("<html><body>Подозрительная активность</body></html>");

        var detector = new BlockDetector(NullLogger<BlockDetector>.Instance);

        var act = () => detector.EnsureNotBlockedAsync(page.Object, response: null, new Uri("https://www.ozon.ru/product/123"));

        await act.Should().ThrowAsync<OzonBlockedException>()
            .WithMessage("*HTML markers*");
    }

    [Fact]
    public async Task EnsureNotBlockedAsync_DoesNotThrowForExpectedOzonPage()
    {
        var page = new Mock<IPage>();
        var response = new Mock<IResponse>();

        page.SetupGet(x => x.Url).Returns("https://www.ozon.ru/product/123");
        page.Setup(x => x.ContentAsync()).ReturnsAsync("<html><body><h1>Product</h1></body></html>");
        response.SetupGet(x => x.Status).Returns(200);

        var detector = new BlockDetector(NullLogger<BlockDetector>.Instance);

        var act = () => detector.EnsureNotBlockedAsync(page.Object, response.Object, new Uri("https://www.ozon.ru/product/123"));

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("https://www.ozon.ru/captcha")]
    [InlineData("https://www.ozon.ru/robot-check")]
    [InlineData("https://blocked.ozon.ru/")]
    public void ContainsBlockedUrlMarker_ReturnsTrueForKnownMarkers(string url)
    {
        BlockDetector.ContainsBlockedUrlMarker(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("<html>captcha-delivery</html>")]
    [InlineData("<html>access denied</html>")]
    [InlineData("<html>Подозрительная активность</html>")]
    public void ContainsBlockedContent_ReturnsTrueForKnownMarkers(string html)
    {
        BlockDetector.ContainsBlockedContent(html).Should().BeTrue();
    }
}
