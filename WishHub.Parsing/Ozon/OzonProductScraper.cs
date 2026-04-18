using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WishHub.Parsing.Playwright.Contexts;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Parsing.Ozon;

public sealed partial class OzonProductScraper : IOzonProductScraper
{
    private const int MaxAttempts = 3;

    private readonly IFingerprintPool _fingerprintPool;
    private readonly IBrowserContextFactory _contextFactory;
    private readonly ISessionManager _sessionManager;
    private readonly IHumanBehaviorSimulator _humanBehaviorSimulator;
    private readonly IBlockDetector _blockDetector;
    private readonly IProxyProvider _proxyProvider;
    private readonly IRequestScheduler _requestScheduler;
    private readonly ILogger<OzonProductScraper> _logger;

    public OzonProductScraper(
        IFingerprintPool fingerprintPool,
        IBrowserContextFactory contextFactory,
        ISessionManager sessionManager,
        IHumanBehaviorSimulator humanBehaviorSimulator,
        IBlockDetector blockDetector,
        IProxyProvider proxyProvider,
        IRequestScheduler requestScheduler,
        ILogger<OzonProductScraper> logger)
    {
        _fingerprintPool = fingerprintPool;
        _contextFactory = contextFactory;
        _sessionManager = sessionManager;
        _humanBehaviorSimulator = humanBehaviorSimulator;
        _blockDetector = blockDetector;
        _proxyProvider = proxyProvider;
        _requestScheduler = requestScheduler;
        _logger = logger;
    }

    public async Task<OzonProduct> ScrapeProductAsync(string productUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productUrl);
        ct.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(productUrl, UriKind.Absolute, out var expectedUri))
        {
            throw new ArgumentException("Product URL must be an absolute URI.", nameof(productUrl));
        }

        OzonBlockedException? lastBlockedException = null;
        var usedProxyUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var profile = _fingerprintPool.GetRandom();
            var proxyUrl = _proxyProvider.GetNextProxy(usedProxyUrls);
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                usedProxyUrls.Add(proxyUrl);
            }

            var sessionId = BuildSessionId(profile, proxyUrl);
            IBrowserContext? context = null;
            IPage? page = null;
            IBrowser? browser = null;

            try
            {
                _logger.LogDebug("Starting Ozon scrape attempt {Attempt} of {MaxAttempts} for {ProductUrl}.", attempt, MaxAttempts, productUrl);

                var product = await _requestScheduler.ExecuteAsync(async innerCt =>
                {
                    context = await _contextFactory.CreateStealthContextAsync(profile, proxyUrl, innerCt);
                    browser = context.Browser;
                    await _sessionManager.RestoreStateAsync(context, sessionId, innerCt);

                    page = await context.NewPageAsync().WaitAsync(innerCt);
                    await _sessionManager.WarmUpSessionAsync(context, page, innerCt);

                    var response = await page.GotoAsync(productUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 45_000
                    }).WaitAsync(innerCt);

                    await _blockDetector.EnsureNotBlockedAsync(page, response, expectedUri, innerCt);
                    await _humanBehaviorSimulator.SimulateReadingPauseAsync(innerCt);
                    await _humanBehaviorSimulator.SimulateScrollAsync(page, 400, 1200, innerCt);

                    var extracted = await ExtractProductAsync(page, productUrl, innerCt);
                    await _sessionManager.SaveStateAsync(context, sessionId, innerCt);
                    return extracted;
                }, proxyUrl, ct);

                _logger.LogInformation("Successfully scraped Ozon product {ProductUrl} on attempt {Attempt}.", productUrl, attempt);
                return product;
            }
            catch (OzonBlockedException ex)
            {
                lastBlockedException = ex;
                _sessionManager.MarkBlocked(sessionId);
                _logger.LogWarning(ex, "Ozon blocked scrape attempt {Attempt} for {ProductUrl}. Retrying with a new profile/proxy.", attempt, productUrl);

                if (attempt == MaxAttempts)
                {
                    throw;
                }
            }
            finally
            {
                if (page is not null)
                {
                    await page.CloseAsync().WaitAsync(CancellationToken.None);
                }

                if (context is not null)
                {
                    await context.CloseAsync().WaitAsync(CancellationToken.None);
                }

                if (browser is not null)
                {
                    await browser.CloseAsync().WaitAsync(CancellationToken.None);
                }
            }
        }

        if (lastBlockedException is not null)
        {
            throw lastBlockedException;
        }

        throw new InvalidOperationException("Ozon scraping failed without a captured blocking exception.");
    }

    private async Task<OzonProduct> ExtractProductAsync(IPage page, string productUrl, CancellationToken ct)
    {
        var html = await page.ContentAsync().WaitAsync(ct);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var title = NormalizeText(
            ExtractMetaProperty(document, "og:title")
            ?? ExtractMetaName(document, "title")
            ?? ExtractJsonLdName(document)
            ?? document.DocumentNode.SelectSingleNode("//h1")?.InnerText);

        var price = ExtractPrice(document, html);
        var rating = ExtractRating(document, html);
        var reviewCount = ExtractReviewCount(document, html);
        var description = NormalizeText(
            ExtractMetaName(document, "description")
            ?? ExtractJsonLdDescription(document)
            ?? document.DocumentNode.SelectSingleNode("//*[@data-widget='webDescription']")?.InnerText);
        var images = ExtractImages(document);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Failed to extract Ozon product title.");
        }

        if (!price.HasValue)
        {
            throw new InvalidOperationException("Failed to extract Ozon product price.");
        }

        return new OzonProduct
        {
            Url = productUrl,
            Title = title,
            Price = price.Value,
            Rating = rating,
            ReviewCount = reviewCount,
            Description = description,
            Images = images
        };
    }

    public static string BuildSessionId(FingerprintProfile profile, string? proxyUrl)
    {
        var payload = string.Join('|', profile.UserAgent, profile.Platform, proxyUrl ?? "direct");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string? ExtractMetaProperty(HtmlDocument document, string property) =>
        document.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")?.GetAttributeValue("content", null);

    internal static string? ExtractMetaName(HtmlDocument document, string name) =>
        document.DocumentNode.SelectSingleNode($"//meta[@name='{name}']")?.GetAttributeValue("content", null);

    internal static decimal? ExtractPrice(HtmlDocument document, string html)
    {
        var candidates = new[]
        {
            ExtractMetaProperty(document, "product:price:amount"),
            ExtractJsonLdOfferPrice(document),
            document.DocumentNode.SelectSingleNode("//*[@data-widget='webPrice']")?.InnerText,
            PriceRegex().Match(html).Success ? PriceRegex().Match(html).Groups[1].Value : null
        };

        foreach (var candidate in candidates)
        {
            var parsed = ParseDecimal(candidate);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        return null;
    }

    internal static decimal? ExtractRating(HtmlDocument document, string html)
    {
        var candidates = new[]
        {
            ExtractJsonLdAggregateRatingValue(document),
            document.DocumentNode.SelectSingleNode("//*[@itemprop='ratingValue']")?.GetAttributeValue("content", null),
            RatingRegex().Match(html).Success ? RatingRegex().Match(html).Groups[1].Value : null
        };

        foreach (var candidate in candidates)
        {
            var parsed = ParseDecimal(candidate);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        return null;
    }

    internal static int? ExtractReviewCount(HtmlDocument document, string html)
    {
        var candidates = new[]
        {
            ExtractJsonLdReviewCount(document),
            document.DocumentNode.SelectSingleNode("//*[@itemprop='reviewCount']")?.GetAttributeValue("content", null),
            ReviewCountRegex().Match(html).Success ? ReviewCountRegex().Match(html).Groups[1].Value : null
        };

        foreach (var candidate in candidates)
        {
            var parsed = ParseInt(candidate);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> ExtractImages(HtmlDocument document)
    {
        var urls = new List<string>();
        AddIfAbsolute(urls, ExtractMetaProperty(document, "og:image"));

        foreach (var image in EnumerateJsonLdImages(document))
        {
            AddIfAbsolute(urls, image);
        }

        foreach (var node in document.DocumentNode.SelectNodes("//img[@src or @data-src]") ?? Enumerable.Empty<HtmlNode>())
        {
            AddIfAbsolute(urls, node.GetAttributeValue("src", null));
            AddIfAbsolute(urls, node.GetAttributeValue("data-src", null));
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static string? ExtractJsonLdName(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            if (TryFindStringProperty(json.RootElement, "name", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractJsonLdDescription(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            if (TryFindStringProperty(json.RootElement, "description", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractJsonLdOfferPrice(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            if (TryFindNestedStringProperty(json.RootElement, "offers", "price", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractJsonLdAggregateRatingValue(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            if (TryFindNestedStringProperty(json.RootElement, "aggregateRating", "ratingValue", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractJsonLdReviewCount(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            if (TryFindNestedStringProperty(json.RootElement, "aggregateRating", "reviewCount", out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateJsonLdImages(HtmlDocument document)
    {
        foreach (var json in EnumerateJsonLdDocuments(document))
        {
            foreach (var image in FindImageValues(json.RootElement))
            {
                yield return image;
            }
        }
    }

    private static IEnumerable<JsonDocument> EnumerateJsonLdDocuments(HtmlDocument document)
    {
        foreach (var node in document.DocumentNode.SelectNodes("//script[@type='application/ld+json']") ?? Enumerable.Empty<HtmlNode>())
        {
            var content = node.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            JsonDocument? json = null;
            try
            {
                json = JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
            }

            if (json is not null)
            {
                yield return json;
            }
        }
    }

    private static bool TryFindStringProperty(JsonElement element, string propertyName, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName))
                {
                    value = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.ToString(),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return true;
                    }
                }

                if (TryFindStringProperty(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindStringProperty(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryFindNestedStringProperty(JsonElement element, string parentPropertyName, string nestedPropertyName, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(parentPropertyName) && TryFindStringProperty(property.Value, nestedPropertyName, out value))
                {
                    return true;
                }

                if (TryFindNestedStringProperty(property.Value, parentPropertyName, nestedPropertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindNestedStringProperty(item, parentPropertyName, nestedPropertyName, out value))
                {
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static IEnumerable<string> FindImageValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("image"))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var image = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(image))
                        {
                            yield return image;
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var image = item.GetString();
                                if (!string.IsNullOrWhiteSpace(image))
                                {
                                    yield return image;
                                }
                            }
                        }
                    }
                }

                foreach (var nested in FindImageValues(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindImageValues(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = HtmlEntity.DeEntitize(value);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace("\u2009", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        var match = NumericRegex().Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digitsOnly = string.Concat(value.Where(char.IsDigit));
        return int.TryParse(digitsOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static void AddIfAbsolute(List<string> urls, string? candidate)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            urls.Add(uri.ToString());
        }
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\d+(?:[.,]\\d+)?")]
    private static partial Regex NumericRegex();

    [GeneratedRegex("\"price\"\\s*:\\s*\"?(\\d+[.,]?\\d*)")]
    private static partial Regex PriceRegex();

    [GeneratedRegex("\"ratingValue\"\\s*:\\s*\"?(\\d+[.,]?\\d*)")]
    private static partial Regex RatingRegex();

    [GeneratedRegex("\"reviewCount\"\\s*:\\s*\"?(\\d+)")]
    private static partial Regex ReviewCountRegex();
}
