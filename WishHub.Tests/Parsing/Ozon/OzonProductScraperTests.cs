using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using WishHub.Parsing.Ozon;
using WishHub.Parsing.Playwright.Contexts;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Tests.Parsing.Ozon;

public class OzonProductScraperTests
{
    [Fact]
    public async Task ScrapeProductAsync_ExtractsProductAndPersistsSession()
    {
        var profile = CreateProfile();
        var pool = new Mock<IFingerprintPool>();
        var contextFactory = new Mock<IBrowserContextFactory>();
        var sessionManager = new Mock<ISessionManager>();
        var human = new Mock<IHumanBehaviorSimulator>();
        var blockDetector = new Mock<IBlockDetector>();
        var proxyProvider = new Mock<IProxyProvider>();
        var scheduler = new ImmediateRequestScheduler();
        var browser = new Mock<IBrowser>();
        var context = new Mock<IBrowserContext>();
        var page = new Mock<IPage>();
        var response = new Mock<IResponse>();

        pool.Setup(x => x.GetRandom()).Returns(profile);
        proxyProvider.Setup(x => x.GetNextProxy(It.IsAny<IReadOnlyCollection<string>>())).Returns((string?)null);
        contextFactory.Setup(x => x.CreateStealthContextAsync(profile, null, It.IsAny<CancellationToken>())).ReturnsAsync(context.Object);
        context.SetupGet(x => x.Browser).Returns(browser.Object);
        context.Setup(x => x.NewPageAsync()).ReturnsAsync(page.Object);
        context.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        browser.Setup(x => x.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(Task.CompletedTask);
        page.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>())).Returns(Task.CompletedTask);
        page.Setup(x => x.GotoAsync("https://www.ozon.ru/product/123", It.IsAny<PageGotoOptions>())).ReturnsAsync(response.Object);
        page.Setup(x => x.ContentAsync()).ReturnsAsync(SampleHtml());
        sessionManager.Setup(x => x.RestoreStateAsync(context.Object, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sessionManager.Setup(x => x.WarmUpSessionAsync(context.Object, page.Object, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        sessionManager.Setup(x => x.SaveStateAsync(context.Object, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        human.Setup(x => x.SimulateReadingPauseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        human.Setup(x => x.SimulateScrollAsync(page.Object, 400, 1200, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        blockDetector.Setup(x => x.EnsureNotBlockedAsync(page.Object, response.Object, It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var scraper = new OzonProductScraper(
            pool.Object,
            contextFactory.Object,
            sessionManager.Object,
            human.Object,
            blockDetector.Object,
            proxyProvider.Object,
            scheduler,
            NullLogger<OzonProductScraper>.Instance);

        var product = await scraper.ScrapeProductAsync("https://www.ozon.ru/product/123");

        product.Url.Should().Be("https://www.ozon.ru/product/123");
        product.Title.Should().Be("Test Ozon Product");
        product.Price.Should().Be(1599m);
        product.Rating.Should().Be(4.8m);
        product.ReviewCount.Should().Be(321);
        product.Description.Should().Be("Detailed product description");
        product.Images.Should().Contain("https://cdn.ozon.ru/image-main.jpg");
        product.Images.Should().Contain("https://cdn.ozon.ru/image-2.jpg");

        sessionManager.Verify(x => x.SaveStateAsync(context.Object, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        sessionManager.Verify(x => x.MarkBlocked(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeProductAsync_RetriesWithNewProfileAndProxyAfterBlock()
    {
        var profile1 = CreateProfile("Chrome/145.0.0.0", "Win32");
        var profile2 = CreateProfile("Chrome/146.0.0.0", "MacIntel");
        var pool = new Mock<IFingerprintPool>();
        var contextFactory = new Mock<IBrowserContextFactory>();
        var sessionManager = new Mock<ISessionManager>();
        var human = new Mock<IHumanBehaviorSimulator>();
        var blockDetector = new Mock<IBlockDetector>();
        var proxyProvider = new Mock<IProxyProvider>();
        var scheduler = new ImmediateRequestScheduler();

        var browser1 = new Mock<IBrowser>();
        var browser2 = new Mock<IBrowser>();
        var context1 = new Mock<IBrowserContext>();
        var context2 = new Mock<IBrowserContext>();
        var page1 = new Mock<IPage>();
        var page2 = new Mock<IPage>();
        var response1 = new Mock<IResponse>();
        var response2 = new Mock<IResponse>();

        pool.SetupSequence(x => x.GetRandom())
            .Returns(profile1)
            .Returns(profile2);

        proxyProvider.SetupSequence(x => x.GetNextProxy(It.IsAny<IReadOnlyCollection<string>>()))
            .Returns("http://proxy-1:8080")
            .Returns("http://proxy-2:8080");

        contextFactory.Setup(x => x.CreateStealthContextAsync(profile1, "http://proxy-1:8080", It.IsAny<CancellationToken>())).ReturnsAsync(context1.Object);
        contextFactory.Setup(x => x.CreateStealthContextAsync(profile2, "http://proxy-2:8080", It.IsAny<CancellationToken>())).ReturnsAsync(context2.Object);

        context1.SetupGet(x => x.Browser).Returns(browser1.Object);
        context2.SetupGet(x => x.Browser).Returns(browser2.Object);
        context1.Setup(x => x.NewPageAsync()).ReturnsAsync(page1.Object);
        context2.Setup(x => x.NewPageAsync()).ReturnsAsync(page2.Object);
        context1.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        context2.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        browser1.Setup(x => x.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(Task.CompletedTask);
        browser2.Setup(x => x.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(Task.CompletedTask);
        page1.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>())).Returns(Task.CompletedTask);
        page2.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>())).Returns(Task.CompletedTask);
        page1.Setup(x => x.GotoAsync("https://www.ozon.ru/product/123", It.IsAny<PageGotoOptions>())).ReturnsAsync(response1.Object);
        page2.Setup(x => x.GotoAsync("https://www.ozon.ru/product/123", It.IsAny<PageGotoOptions>())).ReturnsAsync(response2.Object);
        page2.Setup(x => x.ContentAsync()).ReturnsAsync(SampleHtml());

        sessionManager.Setup(x => x.RestoreStateAsync(It.IsAny<IBrowserContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sessionManager.Setup(x => x.WarmUpSessionAsync(It.IsAny<IBrowserContext>(), It.IsAny<IPage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        sessionManager.Setup(x => x.SaveStateAsync(context2.Object, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        human.Setup(x => x.SimulateReadingPauseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        human.Setup(x => x.SimulateScrollAsync(page2.Object, 400, 1200, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        blockDetector
            .SetupSequence(x => x.EnsureNotBlockedAsync(It.IsAny<IPage>(), It.IsAny<IResponse?>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OzonBlockedException("Blocked"))
            .Returns(Task.CompletedTask);

        var scraper = new OzonProductScraper(
            pool.Object,
            contextFactory.Object,
            sessionManager.Object,
            human.Object,
            blockDetector.Object,
            proxyProvider.Object,
            scheduler,
            NullLogger<OzonProductScraper>.Instance);

        var product = await scraper.ScrapeProductAsync("https://www.ozon.ru/product/123");

        product.Title.Should().Be("Test Ozon Product");
        sessionManager.Verify(x => x.MarkBlocked(It.IsAny<string>()), Times.Once);
        contextFactory.Verify(x => x.CreateStealthContextAsync(profile1, "http://proxy-1:8080", It.IsAny<CancellationToken>()), Times.Once);
        contextFactory.Verify(x => x.CreateStealthContextAsync(profile2, "http://proxy-2:8080", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScrapeProductAsync_ThrowsAfterThreeBlockedAttempts()
    {
        var profile = CreateProfile();
        var pool = new Mock<IFingerprintPool>();
        var contextFactory = new Mock<IBrowserContextFactory>();
        var sessionManager = new Mock<ISessionManager>();
        var human = new Mock<IHumanBehaviorSimulator>();
        var blockDetector = new Mock<IBlockDetector>();
        var proxyProvider = new Mock<IProxyProvider>();
        var scheduler = new ImmediateRequestScheduler();

        pool.Setup(x => x.GetRandom()).Returns(profile);
        proxyProvider.Setup(x => x.GetNextProxy(It.IsAny<IReadOnlyCollection<string>>())).Returns((string?)null);
        sessionManager.Setup(x => x.RestoreStateAsync(It.IsAny<IBrowserContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sessionManager.Setup(x => x.WarmUpSessionAsync(It.IsAny<IBrowserContext>(), It.IsAny<IPage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        blockDetector.Setup(x => x.EnsureNotBlockedAsync(It.IsAny<IPage>(), It.IsAny<IResponse?>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OzonBlockedException("Blocked"));

        for (var i = 0; i < 3; i++)
        {
            var browser = new Mock<IBrowser>();
            var context = new Mock<IBrowserContext>();
            var page = new Mock<IPage>();
            var response = new Mock<IResponse>();

            context.SetupGet(x => x.Browser).Returns(browser.Object);
            context.Setup(x => x.NewPageAsync()).ReturnsAsync(page.Object);
            context.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
            browser.Setup(x => x.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(Task.CompletedTask);
            page.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>())).Returns(Task.CompletedTask);
            page.Setup(x => x.GotoAsync("https://www.ozon.ru/product/123", It.IsAny<PageGotoOptions>())).ReturnsAsync(response.Object);

            contextFactory.SetupSequence(x => x.CreateStealthContextAsync(profile, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(context.Object);
        }

        var contexts = Enumerable.Range(0, 3).Select(_ =>
        {
            var browser = new Mock<IBrowser>();
            var context = new Mock<IBrowserContext>();
            var page = new Mock<IPage>();
            var response = new Mock<IResponse>();
            context.SetupGet(x => x.Browser).Returns(browser.Object);
            context.Setup(x => x.NewPageAsync()).ReturnsAsync(page.Object);
            context.Setup(x => x.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
            browser.Setup(x => x.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(Task.CompletedTask);
            page.Setup(x => x.CloseAsync(It.IsAny<PageCloseOptions?>())).Returns(Task.CompletedTask);
            page.Setup(x => x.GotoAsync("https://www.ozon.ru/product/123", It.IsAny<PageGotoOptions>())).ReturnsAsync(response.Object);
            return context;
        }).ToArray();

        contextFactory.SetupSequence(x => x.CreateStealthContextAsync(profile, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contexts[0].Object)
            .ReturnsAsync(contexts[1].Object)
            .ReturnsAsync(contexts[2].Object);

        var scraper = new OzonProductScraper(
            pool.Object,
            contextFactory.Object,
            sessionManager.Object,
            human.Object,
            blockDetector.Object,
            proxyProvider.Object,
            scheduler,
            NullLogger<OzonProductScraper>.Instance);

        var act = () => scraper.ScrapeProductAsync("https://www.ozon.ru/product/123");

        await act.Should().ThrowAsync<OzonBlockedException>();
        sessionManager.Verify(x => x.MarkBlocked(It.IsAny<string>()), Times.Exactly(3));
    }

    [Fact]
    public void BuildSessionId_ReturnsStableHashForProfileAndProxy()
    {
        var profile = CreateProfile();

        var first = OzonProductScraper.BuildSessionId(profile, "http://proxy-1:8080");
        var second = OzonProductScraper.BuildSessionId(profile, "http://proxy-1:8080");
        var third = OzonProductScraper.BuildSessionId(profile, "http://proxy-2:8080");

        first.Should().Be(second);
        third.Should().NotBe(first);
    }

    private static FingerprintProfile CreateProfile(string chromeVersion = "Chrome/146.0.0.0", string platform = "Win32")
    {
        var userAgent = platform == "MacIntel"
            ? $"Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6_1) AppleWebKit/537.36 (KHTML, like Gecko) {chromeVersion} Safari/537.36"
            : $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) {chromeVersion} Safari/537.36";

        return new FingerprintProfile(
            userAgent,
            new ViewportSize { Width = 1920, Height = 1080 },
            "ru-RU",
            "Europe/Moscow",
            platform,
            "Google Inc. (NVIDIA)",
            "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)",
            8,
            8,
            1920,
            1080);
    }

    private static string SampleHtml() => """
<!DOCTYPE html>
<html>
<head>
    <meta property='og:title' content='Test Ozon Product' />
    <meta property='og:image' content='https://cdn.ozon.ru/image-main.jpg' />
    <meta property='product:price:amount' content='1599' />
    <meta name='description' content='Detailed product description' />
    <script type='application/ld+json'>
    {
      "@context": "https://schema.org",
      "@type": "Product",
      "name": "Test Ozon Product",
      "description": "Detailed product description",
      "image": [
        "https://cdn.ozon.ru/image-main.jpg",
        "https://cdn.ozon.ru/image-2.jpg"
      ],
      "offers": {
        "price": "1599"
      },
      "aggregateRating": {
        "ratingValue": "4.8",
        "reviewCount": "321"
      }
    }
    </script>
</head>
<body>
    <h1>Test Ozon Product</h1>
    <div data-widget='webPrice'>1 599 ₽</div>
    <img src='https://cdn.ozon.ru/image-main.jpg' />
    <img data-src='https://cdn.ozon.ru/image-2.jpg' />
</body>
</html>
""";

    private sealed class ImmediateRequestScheduler : IRequestScheduler
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, string? proxyUrl = null, CancellationToken ct = default) =>
            work(ct);
    }
}
