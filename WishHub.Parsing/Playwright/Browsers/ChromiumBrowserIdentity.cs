namespace WishHub.Parsing.Playwright.Browsers;

public sealed record ChromiumBrowserIdentity(
    string FingerprintSeed,
    string Timezone,
    string Locale,
    string Platform,
    string WebGlVendor,
    string WebGlRenderer,
    int HardwareConcurrency,
    int DeviceMemory,
    int ScreenWidth,
    int ScreenHeight,
    string? ProxyUrl);
