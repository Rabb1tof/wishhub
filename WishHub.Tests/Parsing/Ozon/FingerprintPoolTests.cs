using FluentAssertions;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Tests.Parsing.Ozon;

public class FingerprintPoolTests
{
    [Fact]
    public void Profiles_ContainAtLeastFiveRealisticProfiles()
    {
        var pool = new FingerprintPool();

        pool.Profiles.Should().HaveCountGreaterOrEqualTo(5);
        pool.Profiles.Should().OnlyContain(profile => profile.Locale == "ru-RU");
        pool.Profiles.Should().OnlyContain(profile => profile.Timezone == "Europe/Moscow");
        pool.Profiles.Should().OnlyContain(profile => profile.ScreenWidth >= profile.Viewport.Width);
        pool.Profiles.Should().OnlyContain(profile => profile.ScreenHeight >= profile.Viewport.Height);
        pool.Profiles.Should().OnlyContain(profile => profile.Platform == "Win32" || profile.Platform == "MacIntel");
    }

    [Fact]
    public void Profiles_AreInternallyConsistent()
    {
        var pool = new FingerprintPool();

        pool.Profiles.Should().OnlyContain(profile =>
            profile.Platform == "Win32"
                ? profile.UserAgent.Contains("Windows NT 10.0", StringComparison.Ordinal)
                : profile.UserAgent.Contains("Macintosh; Intel Mac OS X", StringComparison.Ordinal));

        pool.Profiles.Should().OnlyContain(profile => profile.SecChUaPlatform == "\"Windows\"" || profile.SecChUaPlatform == "\"macOS\"");
        pool.Profiles.Should().OnlyContain(profile => !string.IsNullOrWhiteSpace(profile.BrowserMajorVersion));
    }

    [Fact]
    public void GetRandom_ReturnsProfileFromPool()
    {
        var pool = new FingerprintPool();

        var profile = pool.GetRandom();

        pool.Profiles.Should().Contain(profile);
    }
}
