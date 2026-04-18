using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Browsers;

public sealed class PlaywrightChromiumBrowserLauncher : IChromiumBrowserLauncher, IAsyncDisposable
{
    private readonly SemaphoreSlim _playwrightLock = new(1, 1);
    private IPlaywright? _playwright;

    public async Task<IBrowser> LaunchAsync(BrowserTypeLaunchOptions options, CancellationToken ct = default)
    {
        await EnsurePlaywrightAsync(ct);
        return await _playwright!.Chromium.LaunchAsync(options).WaitAsync(ct);
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
