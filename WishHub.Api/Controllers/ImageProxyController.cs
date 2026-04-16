using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/images")]
public class ImageProxyController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly IDatabase _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageProxyController> _logger;

    public ImageProxyController(
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<ImageProxyController> logger)
    {
        _httpClient = httpClientFactory.CreateClient("ImageProxy");
        _redis = redis.GetDatabase();
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("proxy")]
    public async Task<IActionResult> Proxy([FromQuery] string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return BadRequest(new { error = "url_required" });
        }

        // Validate URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return BadRequest(new { error = "invalid_url" });
        }

        // Check whitelist
        var allowedDomains = _configuration.GetSection("ImageProxy:AllowedDomains").Get<string[]>() ?? [];
        if (!IsDomainAllowed(uri.Host, allowedDomains))
        {
            _logger.LogWarning("Domain not allowed: {Domain}", uri.Host);
            return Forbid();
        }

        // Generate cache key
        var cacheKey = $"imgproxy:{ComputeHash(url)}";

        // Check Redis cache
        var cachedData = await _redis.StringGetAsync(cacheKey);
        var cachedContentType = await _redis.StringGetAsync($"{cacheKey}:ct");

        if (!cachedData.IsNull && !cachedContentType.IsNull)
        {
            _logger.LogDebug("Cache hit for: {Url}", url);
            return File((byte[])cachedData!, (string)cachedContentType!);
        }

        // Fetch from source
        try
        {
            var timeout = _configuration.GetValue<int>("ImageProxy:RequestTimeoutSeconds", 10);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

            var response = await _httpClient.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch image: {StatusCode} for {Url}", response.StatusCode, url);
                return NotFound(new { error = "image_unavailable" });
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);

            // Cache in Redis
            var ttl = _configuration.GetValue<int>("ImageProxy:CacheTtlHours", 48);
            await _redis.StringSetAsync(cacheKey, bytes, TimeSpan.FromHours(ttl));
            await _redis.StringSetAsync($"{cacheKey}:ct", contentType, TimeSpan.FromHours(ttl));

            _logger.LogDebug("Image cached: {Url}", url);
            return File(bytes, contentType);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout fetching image: {Url}", url);
            return NotFound(new { error = "image_unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching image: {Url}", url);
            return NotFound(new { error = "image_unavailable" });
        }
    }

    private static bool IsDomainAllowed(string host, string[] allowedDomains)
    {
        foreach (var domain in allowedDomains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
