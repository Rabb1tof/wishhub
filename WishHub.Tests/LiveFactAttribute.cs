using Xunit;

namespace WishHub.Tests;

/// <summary>
/// Marks a test that should run only when RUN_LIVE_PARSING_TESTS=1 is set.
/// Otherwise the test is skipped with a clear message.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!TestSettings.RunLiveParsingTests)
        {
            Skip = "Set RUN_LIVE_PARSING_TESTS=1 to run live parsing tests.";
        }
    }
}
