using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WishHub.Parsing.Ozon;

public sealed class RequestScheduler : IRequestScheduler, IDisposable
{
    private readonly SemaphoreSlim _parallelismGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _proxyLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly RequestSchedulerOptions _options;
    private readonly IRandomValueProvider _random;
    private readonly IAsyncDelay _delay;
    private readonly ILogger<RequestScheduler> _logger;
    private bool _disposed;

    public RequestScheduler(
        IOptions<RequestSchedulerOptions> options,
        IRandomValueProvider random,
        IAsyncDelay delay,
        ILogger<RequestScheduler> logger)
    {
        _options = options.Value;
        ValidateOptions(_options);
        _parallelismGate = new SemaphoreSlim(_options.MaxParallelContexts, _options.MaxParallelContexts);
        _random = random;
        _delay = delay;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, string? proxyUrl = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();

        await _parallelismGate.WaitAsync(ct);
        SemaphoreSlim? proxyLock = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                proxyLock = _proxyLocks.GetOrAdd(proxyUrl, static _ => new SemaphoreSlim(1, 1));
                await proxyLock.WaitAsync(ct);
            }

            var jitter = _random.Next(_options.MinStartDelayMs, _options.MaxStartDelayMs + 1);
            _logger.LogDebug(
                "Scheduling Ozon work with jitter {JitterMs}ms. Proxy configured: {HasProxy}",
                jitter,
                !string.IsNullOrWhiteSpace(proxyUrl));

            await _delay.DelayAsync(jitter, ct);
            return await work(ct);
        }
        finally
        {
            proxyLock?.Release();
            _parallelismGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _parallelismGate.Dispose();

        foreach (var proxyLock in _proxyLocks.Values)
        {
            proxyLock.Dispose();
        }

        _proxyLocks.Clear();
    }

    private static void ValidateOptions(RequestSchedulerOptions options)
    {
        if (options.MaxParallelContexts is < 2 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxParallelContexts), "MaxParallelContexts must be between 2 and 5.");
        }

        if (options.MinStartDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MinStartDelayMs), "MinStartDelayMs must be non-negative.");
        }

        if (options.MaxStartDelayMs < options.MinStartDelayMs)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxStartDelayMs), "MaxStartDelayMs must be greater than or equal to MinStartDelayMs.");
        }
    }
}
