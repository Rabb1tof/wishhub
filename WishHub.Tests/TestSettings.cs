using System;

namespace WishHub.Tests;

public static class TestSettings
{
    public static bool RunLiveParsingTests =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_LIVE_PARSING_TESTS"), "1", StringComparison.OrdinalIgnoreCase);
}
