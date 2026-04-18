using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public sealed class SessionManager : ISessionManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<string, OzonSessionMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _stateCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, DateTime> _warmedContexts = new();
    private readonly SessionManagerOptions _options;
    private readonly IRandomValueProvider _random;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        IOptions<SessionManagerOptions> options,
        IRandomValueProvider random,
        ILogger<SessionManager> logger)
    {
        _options = options.Value;
        _random = random;
        _logger = logger;
    }

    public async Task WarmUpSessionAsync(IBrowserContext context, IPage page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(page);
        ct.ThrowIfCancellationRequested();

        _logger.LogDebug("Starting Ozon session warm-up.");

        await page.GotoAsync("https://www.ozon.ru/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        }).WaitAsync(ct);

        await WaitRandomAsync(page, 2_000, 4_001, ct);

        var scrollCount = _random.Next(2, 4);
        for (var i = 0; i < scrollCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var delta = _random.Next(180, 481);
            await page.Mouse.WheelAsync(0, delta).WaitAsync(ct);
            await WaitRandomAsync(page, 250, 751, ct);
        }

        await WaitRandomAsync(page, 1_000, 2_001, ct);

        _warmedContexts[RuntimeHelpers.GetHashCode(context)] = DateTime.UtcNow;
        _logger.LogDebug("Completed Ozon session warm-up with {ScrollCount} scrolls.", scrollCount);
    }

    public async Task SaveStateAsync(IBrowserContext context, string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ct.ThrowIfCancellationRequested();

        var stateJson = await context.StorageStateAsync(new BrowserContextStorageStateOptions()).WaitAsync(ct);
        var metadata = GetOrCreateMetadata(sessionId);
        CopyWarmUpMetadata(context, metadata);

        Directory.CreateDirectory(_options.StateDirectory);
        await File.WriteAllTextAsync(metadata.StatePath, stateJson, ct);

        _stateCache[sessionId] = stateJson;
        metadata.LastStateSavedAtUtc = DateTime.UtcNow;

        _logger.LogDebug("Saved Ozon session state for {SessionId} to {StatePath}.", sessionId, metadata.StatePath);
    }

    public async Task<bool> RestoreStateAsync(IBrowserContext context, string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ct.ThrowIfCancellationRequested();

        var metadata = GetOrCreateMetadata(sessionId);
        CopyWarmUpMetadata(context, metadata);
        if (metadata.IsBlocked)
        {
            _logger.LogDebug("Skipping restore for blocked Ozon session {SessionId}.", sessionId);
            return false;
        }

        var stateJson = await GetStateAsync(sessionId, metadata.StatePath, ct);
        if (stateJson is null)
        {
            _logger.LogDebug("No stored Ozon session state found for {SessionId}.", sessionId);
            return false;
        }

        var state = JsonSerializer.Deserialize<BrowserStorageState>(stateJson, JsonOptions);
        if (state is null)
        {
            _logger.LogWarning("Stored Ozon session state for {SessionId} could not be deserialized.", sessionId);
            return false;
        }

        if (state.Cookies.Count > 0)
        {
            await context.AddCookiesAsync(state.Cookies.Select(ToCookie)).WaitAsync(ct);
        }

        foreach (var origin in state.Origins.Where(origin => origin.LocalStorage.Count > 0))
        {
            ct.ThrowIfCancellationRequested();
            var page = await context.NewPageAsync().WaitAsync(ct);
            try
            {
                await page.GotoAsync(origin.Origin, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                }).WaitAsync(ct);

                await page.EvaluateAsync(
                    @"entries => {
                        for (const entry of entries) {
                            window.localStorage.setItem(entry.name, entry.value);
                        }
                    }",
                    origin.LocalStorage).WaitAsync(ct);
            }
            finally
            {
                await page.CloseAsync().WaitAsync(ct);
            }
        }

        metadata.LastStateRestoredAtUtc = DateTime.UtcNow;
        _logger.LogDebug("Restored Ozon session state for {SessionId}.", sessionId);
        return true;
    }

    public bool TryGetMetadata(string sessionId, out OzonSessionMetadata? metadata)
    {
        if (_metadata.TryGetValue(sessionId, out var found))
        {
            metadata = found;
            return true;
        }

        metadata = null;
        return false;
    }

    public void MarkBlocked(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var metadata = GetOrCreateMetadata(sessionId);
        metadata.IsBlocked = true;

        _logger.LogInformation("Marked Ozon session {SessionId} as blocked.", sessionId);
    }

    private async Task WaitRandomAsync(IPage page, int minInclusive, int maxExclusive, CancellationToken ct)
    {
        var delay = _random.Next(minInclusive, maxExclusive);
        ct.ThrowIfCancellationRequested();
        await page.WaitForTimeoutAsync(delay).WaitAsync(ct);
    }

    private OzonSessionMetadata GetOrCreateMetadata(string sessionId)
    {
        return _metadata.GetOrAdd(sessionId, static (id, stateDirectory) => new OzonSessionMetadata
        {
            SessionId = id,
            StatePath = Path.Combine(stateDirectory, $"{id}.json")
        }, _options.StateDirectory);
    }

    private async Task<string?> GetStateAsync(string sessionId, string path, CancellationToken ct)
    {
        if (_stateCache.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var fromFile = await File.ReadAllTextAsync(path, ct);
        _stateCache[sessionId] = fromFile;
        return fromFile;
    }

    private void CopyWarmUpMetadata(IBrowserContext context, OzonSessionMetadata metadata)
    {
        var contextKey = RuntimeHelpers.GetHashCode(context);
        if (_warmedContexts.TryGetValue(contextKey, out var warmedUpAtUtc))
        {
            metadata.WarmedUpAtUtc ??= warmedUpAtUtc;
        }
    }

    private static Cookie ToCookie(BrowserStorageCookie cookie)
    {
        return new Cookie
        {
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = cookie.Domain,
            Path = cookie.Path,
            Expires = cookie.Expires.HasValue ? (float)cookie.Expires.Value : null,
            HttpOnly = cookie.HttpOnly,
            Secure = cookie.Secure,
            SameSite = cookie.SameSite switch
            {
                "Strict" => SameSiteAttribute.Strict,
                "Lax" => SameSiteAttribute.Lax,
                "None" => SameSiteAttribute.None,
                _ => null
            }
        };
    }

    private sealed class BrowserStorageState
    {
        public List<BrowserStorageCookie> Cookies { get; init; } = [];

        public List<BrowserStorageOrigin> Origins { get; init; } = [];
    }

    private sealed class BrowserStorageCookie
    {
        public required string Name { get; init; }

        public required string Value { get; init; }

        public required string Domain { get; init; }

        public required string Path { get; init; }

        public double? Expires { get; init; }

        public bool HttpOnly { get; init; }

        public bool Secure { get; init; }

        public string? SameSite { get; init; }
    }

    private sealed class BrowserStorageOrigin
    {
        public required string Origin { get; init; }

        public List<BrowserStorageEntry> LocalStorage { get; init; } = [];
    }

    private sealed class BrowserStorageEntry
    {
        public required string Name { get; init; }

        public required string Value { get; init; }
    }
}
