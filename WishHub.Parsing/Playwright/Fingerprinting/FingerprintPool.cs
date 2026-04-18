using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Fingerprinting;

public sealed class FingerprintPool : IFingerprintPool
{
    private static readonly IReadOnlyList<FingerprintProfile> DefaultProfiles =
    [
        new(
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1920, Height = 1080 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "Win32",
            webGlVendor: "Google Inc. (NVIDIA)",
            webGlRenderer: "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)",
            hardwareConcurrency: 8,
            deviceMemory: 8,
            screenWidth: 1920,
            screenHeight: 1080),
        new(
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1366, Height = 768 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "Win32",
            webGlVendor: "Google Inc.",
            webGlRenderer: "ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0, D3D11)",
            hardwareConcurrency: 4,
            deviceMemory: 8,
            screenWidth: 1366,
            screenHeight: 768),
        new(
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1536, Height = 864 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "Win32",
            webGlVendor: "Google Inc.",
            webGlRenderer: "ANGLE (AMD, AMD Radeon(TM) Graphics Direct3D11 vs_5_0 ps_5_0, D3D11)",
            hardwareConcurrency: 6,
            deviceMemory: 16,
            screenWidth: 1536,
            screenHeight: 864),
        new(
            userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1728, Height = 1117 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "MacIntel",
            webGlVendor: "Google Inc. (Apple)",
            webGlRenderer: "ANGLE (Apple, ANGLE Metal Renderer: Apple M2 Pro, Unspecified Version)",
            hardwareConcurrency: 8,
            deviceMemory: 16,
            screenWidth: 1792,
            screenHeight: 1120),
        new(
            userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_6_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1512, Height = 982 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "MacIntel",
            webGlVendor: "Google Inc. (Apple)",
            webGlRenderer: "ANGLE (Apple, ANGLE Metal Renderer: Apple M1, Unspecified Version)",
            hardwareConcurrency: 8,
            deviceMemory: 8,
            screenWidth: 1512,
            screenHeight: 982),
        new(
            userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5_0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new ViewportSize { Width = 1440, Height = 900 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "MacIntel",
            webGlVendor: "Google Inc. (Apple)",
            webGlRenderer: "ANGLE (Apple, ANGLE Metal Renderer: Apple M3, Unspecified Version)",
            hardwareConcurrency: 12,
            deviceMemory: 16,
            screenWidth: 1440,
            screenHeight: 900)
    ];

    public IReadOnlyList<FingerprintProfile> Profiles => DefaultProfiles;

    public FingerprintProfile GetRandom() => Profiles[Random.Shared.Next(Profiles.Count)];
}
