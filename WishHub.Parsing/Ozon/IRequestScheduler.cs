namespace WishHub.Parsing.Ozon;

public interface IRequestScheduler
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, string? proxyUrl = null, CancellationToken ct = default);
}
