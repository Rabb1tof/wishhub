namespace WishHub.Parsing.Ozon;

public sealed class AsyncDelay : IAsyncDelay
{
    public Task DelayAsync(int millisecondsDelay, CancellationToken ct = default) =>
        Task.Delay(millisecondsDelay, ct);
}
