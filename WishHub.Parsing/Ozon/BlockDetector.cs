using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public sealed class BlockDetector : IBlockDetector
{
    private static readonly string[] BlockedUrlMarkers = ["captcha", "robot", "blocked"];

    private static readonly string[] BlockedContentMarkers =
    [
        "подозрительная активность",
        "captcha-delivery",
        "access denied"
    ];

    private readonly ILogger<BlockDetector> _logger;

    public BlockDetector(ILogger<BlockDetector> logger)
    {
        _logger = logger;
    }

    public async Task EnsureNotBlockedAsync(IPage page, IResponse? response, Uri expectedUri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(expectedUri);
        ct.ThrowIfCancellationRequested();

        var currentUrl = page.Url ?? string.Empty;

        if (ContainsBlockedUrlMarker(currentUrl))
        {
            ThrowBlocked($"Ozon block detected by URL marker at '{currentUrl}'.", currentUrl, response?.Status);
        }

        if (!IsExpectedDomain(currentUrl, expectedUri))
        {
            ThrowBlocked(
                $"Ozon redirected to unexpected domain. Expected '{expectedUri.Host}', got '{currentUrl}'.",
                currentUrl,
                response?.Status);
        }

        if (response is not null && response.Status != 200)
        {
            ThrowBlocked($"Ozon returned unexpected status code {response.Status}.", currentUrl, response.Status);
        }

        var html = await page.ContentAsync().WaitAsync(ct);
        if (ContainsBlockedContent(html))
        {
            ThrowBlocked("Ozon block detected by blocked-page HTML markers.", currentUrl, response?.Status);
        }
    }

    public static bool ContainsBlockedUrlMarker(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return BlockedUrlMarkers.Any(marker => url.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ContainsBlockedContent(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return BlockedContentMarkers.Any(marker => html.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsExpectedDomain(string? currentUrl, Uri expectedUri)
    {
        if (string.IsNullOrWhiteSpace(currentUrl) || !Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
        {
            return false;
        }

        return IsAllowedHost(currentUri.Host) && IsAllowedHost(expectedUri.Host);
    }

    private static bool IsAllowedHost(string host)
    {
        return host.Equals("ozon.ru", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ozon.ru", StringComparison.OrdinalIgnoreCase)
            || host.Equals("ozon.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ozon.com", StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowBlocked(string message, string? currentUrl, int? statusCode)
    {
        _logger.LogWarning("{Message} CurrentUrl={CurrentUrl} StatusCode={StatusCode}", message, currentUrl, statusCode);
        throw new OzonBlockedException(message, currentUrl, statusCode);
    }
}
