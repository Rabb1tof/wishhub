using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WishHub.Parsing.Playwright.Browsers;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Parsing.Playwright.Stealth;

namespace WishHub.Parsing.Playwright.Contexts;

public sealed class BrowserContextFactory : IBrowserContextFactory
{
    private static readonly string[] ChromiumArgs =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-dev-shm-usage",
        "--no-sandbox",
        "--disable-features=IsolateOrigins",
        "--disable-site-isolation-trials"
    ];

    private readonly IChromiumBrowserLauncher _browserLauncher;
    private readonly IStealthInitScriptFactory _initScriptFactory;
    private readonly ILogger<BrowserContextFactory> _logger;

    public BrowserContextFactory(
        IChromiumBrowserLauncher browserLauncher,
        IStealthInitScriptFactory initScriptFactory,
        ILogger<BrowserContextFactory> logger)
    {
        _browserLauncher = browserLauncher;
        _initScriptFactory = initScriptFactory;
        _logger = logger;
    }

    public async Task<IBrowserContext> CreateStealthContextAsync(
        FingerprintProfile profile,
        string? proxyUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ct.ThrowIfCancellationRequested();

        _logger.LogDebug("Creating stealth browser context for platform {Platform} with proxy configured: {HasProxy}",
            profile.Platform,
            !string.IsNullOrWhiteSpace(proxyUrl));

        var browser = await _browserLauncher.LaunchAsync(CreateLaunchOptions(proxyUrl), ct);

        var contextOptions = new BrowserNewContextOptions
        {
            UserAgent = profile.UserAgent,
            ViewportSize = profile.Viewport,
            Locale = profile.Locale,
            TimezoneId = profile.Timezone,
            IgnoreHTTPSErrors = true,
            BypassCSP = true
        };

        var context = await browser.NewContextAsync(contextOptions).WaitAsync(ct);

        await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = profile.AcceptLanguage,
            ["Sec-CH-UA"] = profile.SecChUa,
            ["Sec-CH-UA-Platform"] = profile.SecChUaPlatform,
            ["Sec-CH-UA-Mobile"] = "?0"
        }).WaitAsync(ct);

        await context.AddInitScriptAsync(_initScriptFactory.Create(profile)).WaitAsync(ct);

        return context;
    }

    public static BrowserTypeLaunchOptions CreateLaunchOptions(string? proxyUrl)
    {
        var options = new BrowserTypeLaunchOptions
        {
            Channel = "chromium",
            Headless = !string.Equals(
                Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS"),
                "false",
                StringComparison.OrdinalIgnoreCase),
            Args = ChromiumArgs
        };

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            options.Proxy = new Proxy
            {
                Server = proxyUrl
            };
        }

        return options;
    }
}
