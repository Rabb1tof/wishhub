using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Browsers;

public sealed class PlaywrightChromiumBrowserLauncher : IChromiumBrowserLauncher, IAsyncDisposable
{
    private readonly SemaphoreSlim _playwrightLock = new(1, 1);
    private IPlaywright? _playwright;

    public async Task<IBrowser> LaunchAsync(
        BrowserTypeLaunchOptions options,
        ChromiumBrowserIdentity? identity = null,
        CancellationToken ct = default)
    {
        await EnsurePlaywrightAsync(ct);

        var cdpUrl = Environment.GetEnvironmentVariable("CLOAKBROWSER_CDP_URL");
        if (!string.IsNullOrWhiteSpace(cdpUrl))
        {
            return await _playwright!.Chromium.ConnectOverCDPAsync(BuildCloakBrowserEndpoint(cdpUrl, identity)).WaitAsync(ct);
        }

        return await _playwright!.Chromium.LaunchAsync(options).WaitAsync(ct);
    }

    private static string BuildCloakBrowserEndpoint(string cdpUrl, ChromiumBrowserIdentity? identity)
    {
        if (identity is null)
        {
            return cdpUrl;
        }

        var separator = cdpUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var query = new Dictionary<string, string?>
        {
            ["fingerprint"] = BuildFingerprintSeed(identity.FingerprintSeed),
            ["timezone"] = identity.Timezone,
            ["locale"] = identity.Locale,
            ["platform"] = identity.Platform,
            ["gpu-vendor"] = identity.WebGlVendor,
            ["gpu-renderer"] = identity.WebGlRenderer,
            ["hardware-concurrency"] = identity.HardwareConcurrency.ToString(),
            ["device-memory"] = identity.DeviceMemory.ToString(),
            ["screen-width"] = identity.ScreenWidth.ToString(),
            ["screen-height"] = identity.ScreenHeight.ToString(),
            ["proxy"] = identity.ProxyUrl
        };

        return cdpUrl + separator + string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }

    private static string BuildFingerprintSeed(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt32(bytes, 0).ToString();
    }

    private async Task EnsurePlaywrightAsync(CancellationToken ct)
    {
        if (_playwright is not null)
        {
            return;
        }

        await _playwrightLock.WaitAsync(ct);
        try
        {
            if (_playwright is null)
            {
                _playwright = await Microsoft.Playwright.Playwright.CreateAsync().WaitAsync(ct);
            }
        }
        finally
        {
            _playwrightLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        _playwright = null;
        _playwrightLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
