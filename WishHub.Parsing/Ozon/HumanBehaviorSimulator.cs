using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public sealed class HumanBehaviorSimulator : IHumanBehaviorSimulator
{
    private readonly IRandomValueProvider _random;
    private readonly IAsyncDelay _delay;
    private readonly ILogger<HumanBehaviorSimulator> _logger;

    public HumanBehaviorSimulator(
        IRandomValueProvider random,
        IAsyncDelay delay,
        ILogger<HumanBehaviorSimulator> logger)
    {
        _random = random;
        _delay = delay;
        _logger = logger;
    }

    public async Task SimulateMouseMovementAsync(IPage page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ct.ThrowIfCancellationRequested();

        var movementCount = _random.Next(3, 6);
        var currentX = _random.Next(60, 220);
        var currentY = _random.Next(80, 240);

        _logger.LogDebug("Simulating {MovementCount} curved mouse movements.", movementCount);

        await page.Mouse.MoveAsync(currentX, currentY, new MouseMoveOptions
        {
            Steps = _random.Next(4, 9)
        }).WaitAsync(ct);

        for (var movementIndex = 0; movementIndex < movementCount; movementIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var endX = _random.Next(240, 1440);
            var endY = _random.Next(160, 920);
            var controlX = ((currentX + endX) / 2) + _random.Next(-180, 181);
            var controlY = ((currentY + endY) / 2) + _random.Next(-140, 141);

            foreach (var t in new[] { 0.33d, 0.66d, 1.0d })
            {
                var pointX = CalculateQuadraticBezier(currentX, controlX, endX, t);
                var pointY = CalculateQuadraticBezier(currentY, controlY, endY, t);

                await page.Mouse.MoveAsync(pointX, pointY, new MouseMoveOptions
                {
                    Steps = _random.Next(8, 18)
                }).WaitAsync(ct);
            }

            currentX = endX;
            currentY = endY;
            await RandomDelayAsync(60, 181, ct);
        }
    }

    public async Task SimulateScrollAsync(IPage page, int minPx, int maxPx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (minPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minPx), "Minimum scroll distance must be positive.");
        }

        if (maxPx < minPx)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPx), "Maximum scroll distance must be greater than or equal to minimum scroll distance.");
        }

        ct.ThrowIfCancellationRequested();

        var targetDistance = _random.Next(minPx, maxPx + 1);
        var totalScrolled = 0;

        _logger.LogDebug("Simulating scroll for target distance {TargetDistance}px.", targetDistance);

        while (totalScrolled < targetDistance)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = targetDistance - totalScrolled;
            var chunk = Math.Min(remaining, _random.Next(80, 201));

            await page.Mouse.WheelAsync(0, chunk).WaitAsync(ct);
            totalScrolled += chunk;

            await RandomDelayAsync(100, 301, ct);
        }
    }

    public async Task RandomDelayAsync(int minMs, int maxMs, CancellationToken ct = default)
    {
        if (minMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minMs), "Minimum delay must be non-negative.");
        }

        if (maxMs < minMs)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMs), "Maximum delay must be greater than or equal to minimum delay.");
        }

        ct.ThrowIfCancellationRequested();

        var delay = _random.Next(minMs, maxMs + 1);
        await _delay.DelayAsync(delay, ct);
    }

    public Task SimulateReadingPauseAsync(CancellationToken ct = default) =>
        RandomDelayAsync(3_000, 8_000, ct);

    private static float CalculateQuadraticBezier(int start, int control, int end, double t)
    {
        var oneMinusT = 1d - t;
        var value = (oneMinusT * oneMinusT * start)
            + (2d * oneMinusT * t * control)
            + (t * t * end);

        return (float)value;
    }
}
