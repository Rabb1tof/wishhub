using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public interface ISessionManager
{
    Task WarmUpSessionAsync(IBrowserContext context, IPage page, CancellationToken ct = default);

    Task SaveStateAsync(IBrowserContext context, string sessionId, CancellationToken ct = default);

    Task<bool> RestoreStateAsync(IBrowserContext context, string sessionId, CancellationToken ct = default);

    bool TryGetMetadata(string sessionId, out OzonSessionMetadata? metadata);

    void MarkBlocked(string sessionId);
}
