using Microsoft.Playwright;

namespace WishHub.Api;

public class BrowserProvider : IDisposable
{
    private readonly ILogger<BrowserProvider> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly Lock _lock = new();

    public IBrowser? Browser
    {
        get
        {
            // Проверяем жив ли браузер
            if (_browser?.IsConnected == false)
            {
                _logger.LogWarning("Browser disconnected, attempting to restart...");
                RestartBrowser();
            }
            return _browser;
        }
    }

    public BrowserProvider(ILogger<BrowserProvider> logger)
    {
        _logger = logger;
        InitializeBrowser();
    }

    private void InitializeBrowser()
    {
        try
        {
            _logger.LogInformation("Initializing Playwright...");
            _playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            _logger.LogInformation("Playwright created successfully");

            LaunchBrowser();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Playwright browser. Fallback to HTTP parsers only.");
            _browser = null;
        }
    }

    private void LaunchBrowser()
    {
        lock (_lock)
        {
            try
            {
                var headlessEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS");
                var headless = !string.Equals(headlessEnv, "false", StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation("Launching Chromium (headless={Headless})...", headless);

                _browser = _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-dev-shm-usage",
                        "--disable-features=IsolateOrigins,site-per-process",
                        "--disable-web-security",
                        "--disable-setuid-sandbox",
                        "--disable-accelerated-2d-canvas",
                        "--disable-gpu",
                        "--window-size=1920,1080",
                        "--start-maximized",
                        "--hide-scrollbars",
                        "--disable-notifications",
                        "--disable-extensions",
                        "--force-color-profile=srgb",
                        "--metrics-recording-only",
                        "--safebrowsing-disable-auto-update",
                        "--password-store=basic",
                        "--use-mock-keychain",
                        "--enable-features=NetworkService,NetworkServiceInProcess"
                    }
                }).GetAwaiter().GetResult();

                _logger.LogInformation("Chromium launched successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch Chromium");
                _browser = null;
            }
        }
    }

    private void RestartBrowser()
    {
        lock (_lock)
        {
            try
            {
                _logger.LogInformation("Restarting browser...");
                _browser?.CloseAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing old browser during restart");
            }

            LaunchBrowser();
        }
    }

    public void Dispose()
    {
        try
        {
            _browser?.CloseAsync().GetAwaiter().GetResult();
            _playwright?.Dispose();
        }
        catch { /* best effort */ }
    }
}
