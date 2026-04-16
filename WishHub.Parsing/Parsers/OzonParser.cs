using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Models;
using Microsoft.Playwright;

namespace WishHub.Parsing.Parsers;

public class OzonParser : IProductParser
{
    private readonly HttpClient _httpClient;
    private readonly IBrowser? _browser;
    private const string PlaywrightUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";

    public OzonParser(HttpClient httpClient, IBrowser? browser = null)
    {
        _httpClient = httpClient;
        _browser = browser;
    }

    public bool CanParse(string url) =>
        url.Contains("ozon.ru", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("ozon.com", StringComparison.OrdinalIgnoreCase);

    public async Task<ParseResult> ParseAsync(string url, CancellationToken ct = default)
    {
        // Prefer Playwright (менее заметен как бот), затем HTTP fallback
        if (_browser != null)
        {
            var playwrightResult = await TryParseViaPlaywrightAsync(url, ct);
            if (playwrightResult.Success)
            {
                return playwrightResult;
            }
        }

        // HTTP fallback
        var httpResult = await TryParseViaHttpAsync(url, ct);
        if (httpResult.Success)
        {
            return httpResult;
        }

        // If Playwright failed and HTTP failed, return the most informative
        return _browser != null ? await TryParseViaPlaywrightAsync(url, ct) : httpResult;
    }

    private async Task<ParseResult> TryParseViaHttpAsync(string url, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Add("Pragma", "no-cache");
            request.Headers.Add("Referer", "https://www.ozon.ru/");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Try to get name from og:title
            var name = ExtractMetaTag(doc, "og:title") 
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim();

            // Try to get image from og:image
            var imageUrl = ExtractMetaTag(doc, "og:image");

            // Try to extract price - Ozon uses various selectors
            var price = ExtractPrice(doc);

            if (!string.IsNullOrEmpty(name) && price.HasValue)
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

            return new ParseResult
            {
                Success = false,
                ErrorMessage = "Could not extract all required fields via HTTP",
                Source = ProductSource.Ozon
            };
        }
        catch (Exception ex)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = $"HTTP parsing failed: {ex.Message}",
                Source = ProductSource.Ozon
            };
        }
    }

    private async Task<ParseResult> TryParseViaPlaywrightAsync(string url, CancellationToken ct)
    {
        if (_browser == null)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = "Browser not available",
                Source = ProductSource.Ozon
            };
        }

        try
        {
            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = PlaywrightUserAgent,
                Locale = "ru-RU",
                TimezoneId = "Europe/Moscow",
                ViewportSize = new ViewportSize { Width = 1400, Height = 900 },
                BypassCSP = true
            });

            await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"
            });

            var page = await context.NewPageAsync();
            try
            {
                // Зайти на главную, чтобы получить cookies/anti-bot JS
                await page.GotoAsync("https://www.ozon.ru/", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 15000
                });
                await page.WaitForTimeoutAsync(800);

                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 45000
                });

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
                {
                    Timeout = 10000
                });

                // Give SPA widgets a short time to hydrate
                await page.WaitForTimeoutAsync(1500);

                var content = await page.ContentAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(content);

                var name = ExtractMetaTag(doc, "og:title")
                    ?? (await page.Locator("h1").First.TextContentAsync())?.Trim();

                var imageUrl = ExtractMetaTag(doc, "og:image");
                var price = ExtractPrice(doc);

                if (!price.HasValue)
                {
                    var priceText = await page.EvaluateAsync<string?>(
                        "() => document.querySelector('[data-widget=\\\"webPrice\\\"]')?.textContent");
                    price = ParsePriceFromText(priceText);
                }

                return new ParseResult
                {
                    Success = !string.IsNullOrEmpty(name) && price.HasValue,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.Ozon
                };
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = $"Playwright parsing failed: {ex.Message}",
                Source = ProductSource.Ozon
            };
        }
    }

    private static string? ExtractMetaTag(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']");
        return node?.GetAttributeValue("content", null);
    }

    private static decimal? ExtractPrice(HtmlDocument doc)
    {
        // Try various selectors
        var priceText = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'c3021-a1')]")?.InnerText
            ?? doc.DocumentNode.SelectSingleNode("//div[@data-widget='webPrice']")?.InnerText
            ?? ExtractMetaTag(doc, "product:price:amount");

        return ParsePriceFromText(priceText);
    }

    private static decimal? ParsePriceFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        const string pattern = "\\d[\\d\\s\\u00A0\\u2009,\\.]*";
        var matches = Regex.Matches(text, pattern);

        foreach (Match match in matches)
        {
            var normalized = match.Value
                .Replace(" ", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Replace("\u2009", string.Empty);

            int separatorCount = 0;
            int lastSeparatorIndex = -1;
            for (int i = 0; i < normalized.Length; i++)
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
                var integerPart = normalized.Substring(0, lastSeparatorIndex)
                    .Replace(",", string.Empty)
                    .Replace(".", string.Empty);
                var fractionalPart = normalized.Substring(lastSeparatorIndex + 1)
                    .Replace(",", string.Empty)
                    .Replace(".", string.Empty);
                normalized = fractionalPart.Length > 0
                    ? $"{integerPart}.{fractionalPart}"
                    : integerPart;
            }
            else
            {
                normalized = normalized.Replace(",", ".");
            }

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price > 0)
            {
                return price;
            }
        }

        // Fallback to previous cleaning strategy
        var fallback = Regex.Replace(text, @"[^\d,\.]", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(",", ".");

        return decimal.TryParse(fallback, NumberStyles.Number, CultureInfo.InvariantCulture, out var fallbackPrice)
            ? fallbackPrice
            : (decimal?)null;
    }
}
