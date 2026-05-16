using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using WishHub.Parsing.Playwright.Browsers;
using WishHub.Parsing.Playwright.Contexts;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Parsing.Playwright.Stealth;

namespace WishHub.Tests.Parsing.Ozon;

public class BrowserContextFactoryTests
{
    [Fact]
    public async Task CreateStealthContextAsync_ConfiguresContextHeadersAndScript()
    {
        var profile = new FingerprintProfile(
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1920, Height = 1080 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "Win32",
            webGlVendor: "Google Inc. (NVIDIA)",
            webGlRenderer: "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)",
            hardwareConcurrency: 8,
            deviceMemory: 8,
            screenWidth: 1920,
            screenHeight: 1080);

        BrowserTypeLaunchOptions? launchOptions = null;
        ChromiumBrowserIdentity? browserIdentity = null;
        BrowserNewContextOptions? contextOptions = null;
        IDictionary<string, string>? headers = null;
        string? script = null;

        var launcher = new Mock<IChromiumBrowserLauncher>();
        var browser = new Mock<IBrowser>();
        var context = new Mock<IBrowserContext>();
        var scriptFactory = new Mock<IStealthInitScriptFactory>();

        launcher
            .Setup(x => x.LaunchAsync(
                It.IsAny<BrowserTypeLaunchOptions>(),
                It.IsAny<ChromiumBrowserIdentity?>(),
                It.IsAny<CancellationToken>()))
            .Callback<BrowserTypeLaunchOptions, ChromiumBrowserIdentity?, CancellationToken>((options, identity, _) =>
            {
                launchOptions = options;
                browserIdentity = identity;
            })
            .ReturnsAsync(browser.Object);

        browser
            .Setup(x => x.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
            .Callback<BrowserNewContextOptions>(options => contextOptions = options)
            .ReturnsAsync(context.Object);

        context
            .Setup(x => x.SetExtraHTTPHeadersAsync(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Callback<IEnumerable<KeyValuePair<string, string>>>(pairs => headers = pairs.ToDictionary())
            .Returns(Task.CompletedTask);

        context
            .Setup(x => x.AddInitScriptAsync(It.IsAny<string>(), null))
            .Callback<string, string?>((value, _) => script = value)
            .Returns(Task.CompletedTask);

        scriptFactory
            .Setup(x => x.Create(profile))
            .Returns("window.__stealth = true;");

        var factory = new BrowserContextFactory(launcher.Object, scriptFactory.Object, NullLogger<BrowserContextFactory>.Instance);

        var result = await factory.CreateStealthContextAsync(profile, ct: CancellationToken.None);

        result.Should().Be(context.Object);
        launchOptions.Should().NotBeNull();
        launchOptions!.Args.Should().Contain(new[]
        {
            "--disable-blink-features=AutomationControlled",
            "--disable-dev-shm-usage",
            "--no-sandbox",
            "--disable-features=IsolateOrigins",
            "--disable-site-isolation-trials"
        });
        browserIdentity.Should().NotBeNull();
        browserIdentity!.Locale.Should().Be("ru-RU");
        browserIdentity.Timezone.Should().Be("Europe/Moscow");
        browserIdentity.Platform.Should().Be("windows");
        browserIdentity.ProxyUrl.Should().BeNull();
        contextOptions.Should().NotBeNull();
        contextOptions!.UserAgent.Should().Be(profile.UserAgent);
        contextOptions.Locale.Should().Be("ru-RU");
        contextOptions.TimezoneId.Should().Be("Europe/Moscow");
        contextOptions.ViewportSize!.Width.Should().Be(1920);
        contextOptions.ViewportSize.Height.Should().Be(1080);
        headers.Should().NotBeNull();
        headers!["Accept-Language"].Should().Be("ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        headers["Sec-CH-UA"].Should().Be(profile.SecChUa);
        headers["Sec-CH-UA-Platform"].Should().Be("\"Windows\"");
        headers["Sec-CH-UA-Mobile"].Should().Be("?0");
        script.Should().Be("window.__stealth = true;");
    }

    [Fact]
    public void CreateLaunchOptions_WithProxy_ConfiguresProxyAndRequiredArgs()
    {
        var options = BrowserContextFactory.CreateLaunchOptions("http://user:pass@127.0.0.1:8080");

        options.Proxy.Should().NotBeNull();
        options.Proxy!.Server.Should().Be("http://user:pass@127.0.0.1:8080");
        options.Channel.Should().Be("chromium");
        options.Args.Should().Contain("--disable-blink-features=AutomationControlled");
        options.Args.Should().Contain("--disable-site-isolation-trials");
    }
}
