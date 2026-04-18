using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Models;
using WishHub.Parsing.Ozon;
using WishHub.Parsing.Playwright.Fingerprinting;

namespace WishHub.Parsing.Parsers;

public class OzonParser : IProductParser
{
    private readonly HttpClient _httpClient;
    private readonly IOzonProductScraper? _productScraper;
    private readonly IFingerprintPool _fingerprintPool;
    private readonly ILogger<OzonParser>? _logger;

    public OzonParser(
        HttpClient httpClient,
        IOzonProductScraper? productScraper = null,
        IFingerprintPool? fingerprintPool = null,
        ILogger<OzonParser>? logger = null)
    {
        _httpClient = httpClient;
        _productScraper = productScraper;
        _fingerprintPool = fingerprintPool ?? new FingerprintPool();
        _logger = logger;
    }

    public bool CanParse(string url) =>
        url.Contains("ozon.ru", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("ozon.com", StringComparison.OrdinalIgnoreCase);

    public async Task<ParseResult> ParseAsync(string url, CancellationToken ct = default)
    {
        ParseResult? stealthResult = null;

        if (_productScraper is not null)
        {
            stealthResult = await TryParseViaStealthAsync(url, ct);
            if (stealthResult.Success)
            {
                return stealthResult;
            }
        }

        var httpResult = await TryParseViaHttpAsync(url, ct);
        if (httpResult.Success)
        {
            return httpResult;
        }

        return stealthResult ?? httpResult;
    }

    private async Task<ParseResult> TryParseViaStealthAsync(string url, CancellationToken ct)
    {
        try
        {
            var product = await _productScraper!.ScrapeProductAsync(url, ct);

            return new ParseResult
            {
                Success = true,
                Name = product.Title,
                ImageUrl = product.Images.FirstOrDefault(),
                Price = product.Price,
                Currency = "RUB",
                Source = ProductSource.Ozon
            };
        }
        catch (OzonBlockedException ex)
        {
            _logger?.LogWarning(ex, "Ozon stealth scraper was blocked for {Url}. Falling back to HTTP parsing.", url);
            return Failed($"Stealth scraping blocked: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ozon stealth scraping failed for {Url}. Falling back to HTTP parsing.", url);
            return Failed($"Stealth scraping failed: {ex.Message}");
        }
    }

    private async Task<ParseResult> TryParseViaHttpAsync(string url, CancellationToken ct)
    {
        try
        {
            var profile = _fingerprintPool.GetRandom();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", profile.UserAgent);
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Accept-Language", profile.AcceptLanguage);
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Add("Pragma", "no-cache");
            request.Headers.Add("Referer", "https://www.ozon.ru/");
            request.Headers.Add("Sec-Ch-Ua", profile.SecChUa);
            request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.Add("Sec-Ch-Ua-Platform", profile.SecChUaPlatform);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var name = ExtractMetaTag(doc, "og:title")
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim();
            var imageUrl = ExtractMetaTag(doc, "og:image");
            var price = ExtractPrice(doc);

            if (!string.IsNullOrWhiteSpace(name) && price.HasValue)
            {
                return new ParseResult
                {
                    Success = true,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.Ozon
                };
            }

            return Failed("Could not extract all required fields via HTTP");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ozon HTTP fallback failed for {Url}.", url);
            return Failed($"HTTP parsing failed: {ex.Message}");
        }
    }

    private static ParseResult Failed(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
        Source = ProductSource.Ozon
    };

    private static string? ExtractMetaTag(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']");
        return node?.GetAttributeValue("content", null);
    }

    private static decimal? ExtractPrice(HtmlDocument doc)
    {
        var priceText = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'c3021-a1')]")?.InnerText
            ?? doc.DocumentNode.SelectSingleNode("//div[@data-widget='webPrice']")?.InnerText
            ?? ExtractMetaTag(doc, "product:price:amount");

        return ParsePriceFromText(priceText);
    }

    private static decimal? ParsePriceFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const string pattern = "\\d[\\d\\s\\u00A0\\u2009,\\.]*";
        var matches = Regex.Matches(text, pattern);

        foreach (Match match in matches)
        {
            var normalized = match.Value
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
                .Replace("\u2009", string.Empty, StringComparison.Ordinal);

            int separatorCount = 0;
            var lastSeparatorIndex = -1;
            for (var i = 0; i < normalized.Length; i++)
            {
                var ch = normalized[i];
                if (ch == ',' || ch == '.')
                {
                    separatorCount++;
                    lastSeparatorIndex = i;
                }
            }

            if (separatorCount > 1 && lastSeparatorIndex >= 0)
            {
                var integerPart = normalized[..lastSeparatorIndex]
                    .Replace(",", string.Empty, StringComparison.Ordinal)
                    .Replace(".", string.Empty, StringComparison.Ordinal);
                var fractionalPart = normalized[(lastSeparatorIndex + 1)..]
                    .Replace(",", string.Empty, StringComparison.Ordinal)
                    .Replace(".", string.Empty, StringComparison.Ordinal);
                normalized = fractionalPart.Length > 0
                    ? $"{integerPart}.{fractionalPart}"
                    : integerPart;
            }
            else
            {
                normalized = normalized.Replace(",", ".", StringComparison.Ordinal);
            }

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price > 0)
            {
                return price;
            }
        }

        var fallback = Regex.Replace(text, @"[^\d,\.]", string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal);

        return decimal.TryParse(fallback, NumberStyles.Number, CultureInfo.InvariantCulture, out var fallbackPrice)
            ? fallbackPrice
            : null;
    }
}
