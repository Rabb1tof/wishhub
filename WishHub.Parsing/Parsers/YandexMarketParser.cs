using System.Text.RegularExpressions;
using System.Text.Json;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Models;
using Microsoft.Playwright;

namespace WishHub.Parsing.Parsers;

public class YandexMarketParser : IProductParser
{
    private readonly HttpClient _httpClient;
    private readonly IBrowser? _browser;

    public YandexMarketParser(HttpClient httpClient, IBrowser? browser = null)
    {
        _httpClient = httpClient;
        _browser = browser;
    }

    public bool CanParse(string url) =>
        url.Contains("market.yandex.ru", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("market.yandex.com", StringComparison.OrdinalIgnoreCase);

    public async Task<ParseResult> ParseAsync(string url, CancellationToken ct = default)
    {
        // Yandex Market — SPA, HTTP-парсинг почти не даёт данных
        if (_browser != null)
        {
            return await TryParseViaPlaywrightAsync(url, ct);
        }

        // Fallback: HTTP + meta tags
        return await TryParseViaHttpAsync(url, ct);
    }

    private async Task<ParseResult> TryParseViaHttpAsync(string url, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9");

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            // Извлекаем из __NEXT_DATA__ (Next.js SSR state)
            var (name, price, imageUrl) = ExtractFromNextData(html);

            // Fallback: meta tags
            if (string.IsNullOrEmpty(name))
                name = ExtractMetaContent(html, "og:title");
            if (string.IsNullOrEmpty(imageUrl))
                imageUrl = ExtractMetaContent(html, "og:image");
            if (!price.HasValue)
                price = ExtractPriceFromHtml(html);

            if (!string.IsNullOrEmpty(name))
            {
                return new ParseResult
                {
                    Success = true,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.YandexMarket
                };
            }

            return new ParseResult
            {
                Success = false,
                ErrorMessage = "Could not extract product name from HTTP",
                Source = ProductSource.YandexMarket
            };
        }
        catch (Exception ex)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = $"HTTP parsing failed: {ex.Message}",
                Source = ProductSource.YandexMarket
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
                Source = ProductSource.YandexMarket
            };
        }

        try
        {
            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                Locale = "ru-RU",
            });

            await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"
            });

            var page = await context.NewPageAsync();
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 45000
                });

                // Ждём загрузки контента
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                // Ждём появления h1 или цены
                try
                {
                    await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions
                    {
                        Timeout = 10000,
                        State = WaitForSelectorState.Visible
                    });
                }
                catch { }

                await page.WaitForTimeoutAsync(2000);

                // Единственный JS-вызов: __NEXT_DATA__ → DOM fallback
                var extractedJson = await page.EvaluateAsync<string?>(@"() => {
                    let name = null, price = null, image = null;

                    // 1. __NEXT_DATA__ (Next.js SSR state)
                    try {
                        const nd = window.__NEXT_DATA__;
                        const props = nd?.props?.pageProps;
                        if (props) {
                            // Ищем в разных местах структуры Yandex Market
                            const product = props.product || props.productCard || props.initialState?.product
                                || props.gqlData?.product || props.data?.product;
                            if (product) {
                                name = product.name || product.title || null;
                                price = product.price?.value || product.price?.defaultOffer?.price
                                    || product.offers?.[0]?.price || product.priceValue || null;
                                image = product.imageUrl || product.image?.src
                                    || product.photos?.[0]?.url || product.images?.[0]?.url
                                    || product.gallery?.[0]?.url || null;
                            }
                        }
                    } catch {}

                    // 2. DOM fallback для имени
                    if (!name) {
                        const h1 = document.querySelector('h1');
                        if (h1) name = h1.textContent?.trim() || null;
                    }
                    if (!name) {
                        const og = document.querySelector('meta[property=""og:title""]');
                        name = og?.content?.trim() || null;
                    }

                    // 3. DOM fallback для цены
                    if (!price) {
                        const priceSelectors = [
                            '[data-auto=""price-value""]',
                            '[data-auto=""mainPrice""]',
                            '[data-zone-name=""price""]',
                            'span[class*=""price""]',
                            'div[class*=""Price""] span',
                            'span[data-tid=""price""]',
                        ];
                        for (const sel of priceSelectors) {
                            const el = document.querySelector(sel);
                            if (el?.textContent?.trim()) {
                                const m = el.textContent.match(/(\d[\d\s\u00A0\u2009]*)/);
                                if (m) { price = m[1].replace(/[\s\u00A0\u2009]/g, ''); break; }
                            }
                        }
                    }
                    if (!price) {
                        const meta = document.querySelector('meta[property=""product:price:amount""]');
                        if (meta?.content) price = meta.content;
                    }

                    // 4. DOM fallback для картинки — ищем большую версию
                    if (!image) {
                        // Сначала из meta og:image
                        const ogImg = document.querySelector('meta[property=""og:image""]');
                        image = ogImg?.content || null;
                    }
                    if (!image) {
                        // Ищем в галерее товара
                        const imgs = document.querySelectorAll('img');
                        for (const img of imgs) {
                            const src = img.src || img.getAttribute('data-src') || img.getAttribute('data-lazy-src');
                            if (src && (src.includes('market.cdn') || src.includes('avatars.mds') || src.includes('yandex'))) {
                                // Берём самую большую картинку (по размеру в URL)
                                if (!image || src.includes('/orig/') || src.includes('/large/')) {
                                    image = src;
                                }
                            }
                        }
                    }

                    return JSON.stringify({ name, price, image });
                }");

                string? name = null;
                decimal? price = null;
                string? imageUrl = null;

                if (!string.IsNullOrWhiteSpace(extractedJson))
                {
                    using var doc = JsonDocument.Parse(extractedJson);
                    var root = doc.RootElement;

                    name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                    name = name?.Trim();

                    if (root.TryGetProperty("price", out var pProp))
                    {
                        if (pProp.ValueKind == JsonValueKind.Number)
                            price = pProp.GetDecimal();
                        else if (pProp.ValueKind == JsonValueKind.String)
                            price = ParsePriceFromText(pProp.GetString());
                    }

                    imageUrl = root.TryGetProperty("image", out var iProp) ? iProp.GetString() : null;
                    imageUrl = NormalizeImageUrl(imageUrl);
                }

                // Fallback: цена из title страницы
                if (!price.HasValue)
                {
                    var title = await page.TitleAsync();
                    var priceMatch = Regex.Match(title, @"(\d[\d\s\u00A0]+)\s*₽");
                    if (priceMatch.Success)
                    {
                        var priceStr = priceMatch.Groups[1].Value.Replace("\u00A0", "").Replace(" ", "");
                        if (decimal.TryParse(priceStr, out var priceFromTitle))
                            price = priceFromTitle;
                    }
                }

                var success = !string.IsNullOrEmpty(name);
                return new ParseResult
                {
                    Success = success,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.YandexMarket,
                    ErrorMessage = success ? null : "Could not extract product name"
                };
            }
            finally
            {
                await page.CloseAsync();
                await context.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = $"Playwright parsing failed: {ex.Message}",
                Source = ProductSource.YandexMarket
            };
        }
    }

    private static (string? name, decimal? price, string? imageUrl) ExtractFromNextData(string html)
    {
        try
        {
            var match = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""[^>]*>(.*?)</script>", RegexOptions.Singleline);
            if (!match.Success) return (null, null, null);

            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var root = doc.RootElement;

            var props = root
                .GetProperty("props")
                .GetProperty("pageProps");

            // Ищем продукт в разных местах структуры
            JsonElement? product = null;
            if (props.TryGetProperty("product", out var p)) product = p;
            else if (props.TryGetProperty("productCard", out var pc)) product = pc;
            else if (props.TryGetProperty("initialState", out var init)
                     && init.TryGetProperty("product", out var ip)) product = ip;
            else if (props.TryGetProperty("gqlData", out var gql)
                     && gql.TryGetProperty("product", out var gp)) product = gp;

            if (product == null) return (null, null, null);

            string? name = null;
            if (product.Value.TryGetProperty("name", out var n)) name = n.GetString();
            else if (product.Value.TryGetProperty("title", out var t)) name = t.GetString();

            decimal? price = null;
            if (product.Value.TryGetProperty("price", out var priceEl))
            {
                if (priceEl.TryGetProperty("value", out var pv)) price = pv.GetDecimal();
                else if (priceEl.ValueKind == JsonValueKind.Number) price = priceEl.GetDecimal();
            }

            string? imageUrl = null;
            if (product.Value.TryGetProperty("imageUrl", out var img)) imageUrl = img.GetString();
            else if (product.Value.TryGetProperty("image", out var imgObj)
                     && imgObj.TryGetProperty("src", out var imgSrc)) imageUrl = imgSrc.GetString();

            return (name, price, NormalizeImageUrl(imageUrl));
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? ExtractMetaContent(string html, string property)
    {
        var match = Regex.Match(html, $@"<meta\s+property=""{Regex.Escape(property)}""\s+content=""([^""]*)""", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(html, $@"<meta\s+content=""([^""]*)""\s+property=""{Regex.Escape(property)}""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static decimal? ExtractPriceFromHtml(string html)
    {
        // Ищем цену в meta tag
        var metaMatch = Regex.Match(html, @"<meta\s+property=""product:price:amount""\s+content=""([^""]*)""", RegexOptions.IgnoreCase);
        if (metaMatch.Success && decimal.TryParse(metaMatch.Groups[1].Value, out var metaPrice))
            return metaPrice;

        // Ищем в data-auto атрибутах
        var dataMatch = Regex.Match(html, @"data-auto=""price-value""[^>]*>([^<]*)");
        if (dataMatch.Success)
            return ParsePriceFromText(dataMatch.Groups[1].Value);

        return null;
    }

    private static decimal? ParsePriceFromText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var cleaned = Regex.Replace(text, @"[^\d,\.]", "");
        cleaned = cleaned.Replace(",", ".");

        return decimal.TryParse(cleaned, out var price) ? price : null;
    }

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("//")) return $"https:{url}";
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        return url.StartsWith("/") ? $"https://market.yandex.ru{url}" : url;
    }
}
