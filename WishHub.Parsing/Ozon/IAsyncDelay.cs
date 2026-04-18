namespace WishHub.Parsing.Ozon;

public interface IAsyncDelay
{
    Task DelayAsync(int millisecondsDelay, CancellationToken ct = default);
}
