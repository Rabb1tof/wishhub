using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Browsers;

public interface IChromiumBrowserLauncher
{
    Task<IBrowser> LaunchAsync(
        BrowserTypeLaunchOptions options,
        ChromiumBrowserIdentity? identity = null,
        CancellationToken ct = default);
}
