using FluentAssertions;
using WishHub.Parsing.Playwright.Fingerprinting;
using WishHub.Parsing.Playwright.Stealth;

namespace WishHub.Tests.Parsing.Ozon;

public class StealthInitScriptFactoryTests
{
    [Fact]
    public void Create_EmbedsExpectedNavigatorOverrides()
    {
        var profile = new FingerprintProfile(
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
            viewport: new Microsoft.Playwright.ViewportSize { Width = 1920, Height = 1080 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "Win32",
            webGlVendor: "Google Inc. (NVIDIA)",
            webGlRenderer: "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)",
            hardwareConcurrency: 8,
            deviceMemory: 16,
            screenWidth: 1920,
            screenHeight: 1080);

        var factory = new StealthInitScriptFactory();

        var script = factory.Create(profile);

        script.Should().Contain("Navigator.prototype, 'webdriver'");
        script.Should().Contain("Chrome PDF Viewer");
        script.Should().Contain("Widevine Content Decryption Module");
        script.Should().Contain("['ru-RU','ru','en-US','en']".Replace("'", "\""));
        script.Should().Contain("get: () => 8");
        script.Should().Contain("get: () => 16");
        script.Should().Contain("get: () => platform");
        script.Should().Contain("window.chrome = window.chrome ||");
        script.Should().Contain("navigator.permissions.query = (parameters) =>");
        script.Should().Contain("return webGlVendor");
        script.Should().Contain("return webGlRenderer");
    }

    [Fact]
    public void Create_UsesProfileSpecificValues()
    {
        var profile = new FingerprintProfile(
            userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
            viewport: new Microsoft.Playwright.ViewportSize { Width = 1728, Height = 1117 },
            locale: "ru-RU",
            timezone: "Europe/Moscow",
            platform: "MacIntel",
            webGlVendor: "Google Inc. (Apple)",
            webGlRenderer: "ANGLE (Apple, ANGLE Metal Renderer: Apple M2 Pro, Unspecified Version)",
            hardwareConcurrency: 12,
            deviceMemory: 8,
            screenWidth: 1792,
            screenHeight: 1120);

        var factory = new StealthInitScriptFactory();

        var script = factory.Create(profile);

        script.Should().Contain("MacIntel");
        script.Should().Contain("Google Inc. (Apple)");
        script.Should().Contain("Apple M2 Pro");
        script.Should().Contain("get: () => 12");
        script.Should().Contain("get: () => 8");
    }
}
