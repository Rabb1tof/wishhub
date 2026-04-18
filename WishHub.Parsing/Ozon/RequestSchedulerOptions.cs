namespace WishHub.Parsing.Ozon;

public sealed class RequestSchedulerOptions
{
    public int MaxParallelContexts { get; set; } = 3;

    public int MinStartDelayMs { get; set; } = 3_000;

    public int MaxStartDelayMs { get; set; } = 12_000;
}
