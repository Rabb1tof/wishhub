using Microsoft.Playwright;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Parsing.Playwright.Contexts;

public interface IBrowserContextFactory
{
    Task<IBrowserContext> CreateStealthContextAsync(
        FingerprintProfile profile,
        string? proxyUrl = null,
        CancellationToken ct = default);
}
