using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WishHub.Parsing.Playwright.Fingerprinting;

public sealed class FingerprintProfile
{
    private static readonly Regex ChromeVersionRegex = new(@"Chrome\/(?<major>\d+)", RegexOptions.Compiled);

    public FingerprintProfile(
        string userAgent,
        ViewportSize viewport,
        string locale,
        string timezone,
        string platform,
        string webGlVendor,
        string webGlRenderer,
        int hardwareConcurrency,
        int deviceMemory,
        int screenWidth,
        int screenHeight)
    {
        UserAgent = userAgent;
        Viewport = viewport;
        Locale = locale;
        Timezone = timezone;
        Platform = platform;
        WebGLVendor = webGlVendor;
        WebGLRenderer = webGlRenderer;
        HardwareConcurrency = hardwareConcurrency;
        DeviceMemory = deviceMemory;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;

        Validate();
    }

    public string UserAgent { get; }

    public ViewportSize Viewport { get; }

    public string Locale { get; }

    public string Timezone { get; }

    public string Platform { get; }

    public string WebGLVendor { get; }

    public string WebGLRenderer { get; }

    public int HardwareConcurrency { get; }

    public int DeviceMemory { get; }

    public int ScreenWidth { get; }

    public int ScreenHeight { get; }

    public string BrowserMajorVersion
    {
        get
        {
            var match = ChromeVersionRegex.Match(UserAgent);
            if (!match.Success)
            {
                throw new InvalidOperationException($"User-Agent does not contain a Chrome version: {UserAgent}");
            }

            return match.Groups["major"].Value;
        }
    }

    public string SecChUa =>
        $"\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"{BrowserMajorVersion}\", \"Google Chrome\";v=\"{BrowserMajorVersion}\"";

    public string SecChUaPlatform => Platform switch
    {
        "Win32" => "\"Windows\"",
        "MacIntel" => "\"macOS\"",
        _ => throw new InvalidOperationException($"Unsupported platform '{Platform}'.")
    };

    public string AcceptLanguage => $"{Locale},ru;q=0.9,en-US;q=0.8,en;q=0.7";

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new ArgumentException("UserAgent is required.", nameof(UserAgent));
        }

        if (Viewport.Width <= 0 || Viewport.Height <= 0)
        {
            throw new ArgumentException("Viewport must have positive width and height.", nameof(Viewport));
        }

        if (!string.Equals(Locale, "ru-RU", StringComparison.Ordinal))
        {
            throw new ArgumentException("Locale must be ru-RU.", nameof(Locale));
        }

        if (!string.Equals(Timezone, "Europe/Moscow", StringComparison.Ordinal))
        {
            throw new ArgumentException("Timezone must be Europe/Moscow.", nameof(Timezone));
        }

        if (ScreenWidth < Viewport.Width || ScreenHeight < Viewport.Height)
        {
            throw new ArgumentException("Screen size must be greater than or equal to viewport size.");
        }

        if (HardwareConcurrency is not (4 or 6 or 8 or 12))
        {
            throw new ArgumentException("HardwareConcurrency must be one of: 4, 6, 8, 12.", nameof(HardwareConcurrency));
        }

        if (DeviceMemory is not (4 or 8 or 16))
        {
            throw new ArgumentException("DeviceMemory must be one of: 4, 8, 16.", nameof(DeviceMemory));
        }

        if (Platform == "Win32" && !UserAgent.Contains("Windows NT 10.0", StringComparison.Ordinal))
        {
            throw new ArgumentException("Windows profiles must use a Windows Chrome user agent.", nameof(UserAgent));
        }

        if (Platform == "MacIntel" && !UserAgent.Contains("Macintosh; Intel Mac OS X", StringComparison.Ordinal))
        {
            throw new ArgumentException("Mac profiles must use a macOS Chrome user agent.", nameof(UserAgent));
        }

        _ = BrowserMajorVersion;
    }
}
