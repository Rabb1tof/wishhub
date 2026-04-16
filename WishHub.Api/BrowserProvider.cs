using Microsoft.Playwright;

namespace WishHub.Api;

public class BrowserProvider
{
    public IBrowser? Browser { get; }

    public BrowserProvider()
    {
        try
        {
            var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            var headlessEnv = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS");
            var headless = !string.Equals(headlessEnv, "false", StringComparison.OrdinalIgnoreCase);
            Browser = playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-dev-shm-usage",
                    "--disable-features=IsolateOrigins,site-per-process"
                }
            }).GetAwaiter().GetResult();
        }
        catch
        {
            Browser = null; // fallback to HTTP parsers only
        }
    }
}
