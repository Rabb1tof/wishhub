using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Browsers;

public interface IChromiumBrowserLauncher
{
    Task<IBrowser> LaunchAsync(BrowserTypeLaunchOptions options, CancellationToken ct = default);
}
