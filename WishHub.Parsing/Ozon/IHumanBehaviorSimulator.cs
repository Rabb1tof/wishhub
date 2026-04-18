using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public interface IHumanBehaviorSimulator
{
    Task SimulateMouseMovementAsync(IPage page, CancellationToken ct = default);

    Task SimulateScrollAsync(IPage page, int minPx, int maxPx, CancellationToken ct = default);

    Task RandomDelayAsync(int minMs, int maxMs, CancellationToken ct = default);

    Task SimulateReadingPauseAsync(CancellationToken ct = default);
}
