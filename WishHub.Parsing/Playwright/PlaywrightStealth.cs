using Microsoft.Playwright;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Parsing.Playwright.Stealth;

namespace WishHub.Parsing.Playwright;

public static class PlaywrightStealth
{
    private static readonly IFingerprintPool FingerprintPool = new FingerprintPool();
    private static readonly IStealthInitScriptFactory InitScriptFactory = new StealthInitScriptFactory();

    public static IReadOnlyList<FingerprintProfile> BrowserProfiles => FingerprintPool.Profiles;

    public static FingerprintProfile GetRandomProfile() => FingerprintPool.GetRandom();

    public static string CreateInitScript(FingerprintProfile profile) => InitScriptFactory.Create(profile);

    public static async Task<IBrowserContext> CreateContextWithProfileAsync(
        IBrowser browser,
        string storageStatePath,
        FingerprintProfile profile,
        BrowserNewContextOptions? overrides = null)
    {
        var options = new BrowserNewContextOptions
        {
            UserAgent = profile.UserAgent,
            Locale = overrides?.Locale ?? profile.Locale,
            TimezoneId = overrides?.TimezoneId ?? profile.Timezone,
            ViewportSize = overrides?.ViewportSize ?? profile.Viewport,
            BypassCSP = overrides?.BypassCSP ?? true,
            HasTouch = overrides?.HasTouch ?? false,
            IsMobile = overrides?.IsMobile ?? false,
            DeviceScaleFactor = overrides?.DeviceScaleFactor ?? 1,
            Permissions = overrides?.Permissions ?? ["notifications"],
            IgnoreHTTPSErrors = overrides?.IgnoreHTTPSErrors ?? true
        };

        if (File.Exists(storageStatePath))
        {
            options.StorageStatePath = storageStatePath;
        }

        var context = await browser.NewContextAsync(options);

        await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = profile.AcceptLanguage,
            ["Sec-Ch-Ua"] = profile.SecChUa,
            ["Sec-Ch-Ua-Mobile"] = "?0",
            ["Sec-Ch-Ua-Platform"] = profile.SecChUaPlatform
        });

        await context.AddInitScriptAsync(CreateInitScript(profile));

        return context;
    }

    public static Task<IBrowserContext> CreateContextAsync(
        IBrowser browser,
        string storageStatePath,
        BrowserNewContextOptions? overrides = null)
    {
        return CreateContextWithProfileAsync(browser, storageStatePath, BrowserProfiles[0], overrides);
    }

    public static async Task SaveStateAsync(IBrowserContext context, string storageStatePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(storageStatePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await context.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = storageStatePath
            });
        }
        catch
        {
        }
    }

    public static bool IsHeadlessMode()
    {
        var value = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS");
        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
    }

    public static float AdaptiveTimeout(float headlessTimeoutMs = 100_000) =>
        IsHeadlessMode() ? headlessTimeoutMs : 120_000f;

    public static async Task<bool> IsChallengePageAsync(IPage page)
    {
        var url = page.Url ?? string.Empty;
        if (url.Contains("/captcha", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("captcha.yandex", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("smartcaptcha", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/showcaptcha", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var title = await page.TitleAsync();
            return IsChallengeTitle(title);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsChallengeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var low = title.Trim().ToLowerInvariant();
        return low.Contains("доступ ограничен")
            || low.Contains("проверяем, что запросы")
            || low.Contains("проверка браузера")
            || low.Contains("сопоставьте")
            || low.Contains("капча")
            || low.Contains("проверяем")
            || low.Contains("captcha")
            || low.Contains("access denied")
            || low.Contains("just a moment")
            || low.Contains("robot check")
            || low.Contains("are you human")
            || low.Contains("проверка безопасности");
    }

    public static string DefaultStatePath(string siteKey) =>
        Path.Combine(AppContext.BaseDirectory, "playwright-state", $"{siteKey}.json");
}
