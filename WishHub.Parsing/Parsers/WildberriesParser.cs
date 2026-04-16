using System.Text.Json;
using System.Text.RegularExpressions;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Models;
using Microsoft.Playwright;

namespace WishHub.Parsing.Parsers;

public class WildberriesParser : IProductParser
{
    private readonly HttpClient _httpClient;
    private readonly IBrowser? _browser;
    private static readonly int[] Destinations = { -1257786, -1029256, 123585, 1219056, 1306246 };
    private static readonly string[] ApiVersions = { "v2", "v5" };
    private static readonly int[] SppVariants = { 0, 1, 2, 5, 12, 30 };
    private const string ApiRoot = "https://card.wb.ru/cards";
    private const string RegionsParam = "64,75,83,4,38,30,33,70,68,22,31,66,67,48,110,71,114";
    private const string StoresParam = "117673,122258,122259,125238,507,3158,5073,117501,5076";
    private const string PlaywrightUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";

    public WildberriesParser(HttpClient httpClient, IBrowser? browser = null)
    {
        _httpClient = httpClient;
        _browser = browser;
    }

    public bool CanParse(string url) =>
        url.Contains("wildberries.ru", StringComparison.OrdinalIgnoreCase);

    public async Task<ParseResult> ParseAsync(string url, CancellationToken ct = default)
    {
        try
        {
            // Сначала пробуем Playwright (основной путь)
            if (_browser != null)
            {
                return await TryParseViaPlaywrightAsync(url, ct);
            }

            // Если браузера нет, пробуем API как резерв
            var nmId = ExtractNmId(url);
            if (string.IsNullOrEmpty(nmId))
            {
                return new ParseResult
                {
                    Success = false,
                    ErrorMessage = "Could not extract product ID from URL",
                    Source = ProductSource.Wildberries
                };
            }

            HttpResponseMessage? successResponse = null;
            var found = false;

            foreach (var version in ApiVersions)
            {
                foreach (var dest in Destinations)
                {
                    foreach (var spp in SppVariants)
                    {
                        var uri = new Uri($"{ApiRoot}/{version}/detail?appType=1&curr=rub&dest={dest}&regions={RegionsParam}&stores={StoresParam}&pricemarginCoeff=1.0&reg=0&locale=ru&lang=ru&spp={spp}&nm={nmId}");
                        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.Add("User-Agent", PlaywrightUserAgent);
                        request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
                        request.Headers.Add("Cache-Control", "no-cache");
                        request.Headers.Add("Pragma", "no-cache");
                        request.Headers.Add("Referer", "https://www.wildberries.ru/");
                        request.Headers.Add("Origin", "https://www.wildberries.ru");

                        var response = await _httpClient.SendAsync(request, ct);
                        if (response.IsSuccessStatusCode)
                        {
                            successResponse = response;
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        break;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            if (successResponse == null)
            {
                return new ParseResult
                {
                    Success = false,
                    ErrorMessage = "Product not found or API returned error",
                    Source = ProductSource.Wildberries
                };
            }

            using (successResponse)
            {
                var json = await successResponse.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);

                var products = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("products")
                    .EnumerateArray()
                    .ToList();

                if (products.Count == 0)
                {
                    return new ParseResult
                    {
                        Success = false,
                        ErrorMessage = "Product not found",
                        Source = ProductSource.Wildberries
                    };
                }

                var product = products[0];
                var name = product.GetProperty("name").GetString();
                
                // Price is in kopecks (divide by 100)
                var priceKopecks = product.TryGetProperty("salePriceU", out var priceProp) 
                    ? priceProp.GetInt64() 
                    : product.GetProperty("priceU").GetInt64();
                var price = priceKopecks / 100m;

                // Image URL
                var photos = product.GetProperty("photos").EnumerateArray().ToList();
                var imageUrl = photos.Count > 0 
                    ? photos[0].GetProperty("big").GetString() 
                    : null;

                return new ParseResult
                {
                    Success = true,
                    Name = name!,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.Wildberries
                };
            }
        }
        catch (Exception ex)
        {
            return new ParseResult
            {
                Success = false,
                ErrorMessage = $"Parsing failed: {ex.Message}",
                Source = ProductSource.Wildberries
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
                Source = ProductSource.Wildberries
            };
        }

        try
        {
            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = PlaywrightUserAgent,
                Locale = "ru-RU",
                TimezoneId = "Europe/Moscow",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                BypassCSP = true
            });

            await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7"
            });

            var page = await context.NewPageAsync();
            try
            {
                // Сразу идём на страницу товара (без предварительного посещения главной)
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 45000
                });

                // Проверяем что нас не редиректнуло на главную
                var currentUrl = page.Url;
                if (!currentUrl.Contains("catalog/") || currentUrl == "https://www.wildberries.ru/" || currentUrl == "https://wildberries.ru/")
                {
                    // Редирект на главную — пробуем ещё раз с другим User-Agent
                    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                    await page.WaitForTimeoutAsync(2000);
                }

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
                {
                    Timeout = 10000
                });

                // Ждём появления заголовка товара — сигнал что страница загрузилась
                try
                {
                    await page.WaitForSelectorAsync("h2[class*='productTitle']", new PageWaitForSelectorOptions
                    {
                        Timeout = 10000,
                        State = WaitForSelectorState.Visible
                    });
                }
                catch
                {
                    // productTitle не появился — попробуем h1 как fallback
                    try
                    {
                        await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions
                        {
                            Timeout = 5000,
                            State = WaitForSelectorState.Visible
                        });
                    }
                    catch { }
                }

                // Ждём полной загрузки страницы товара
                await page.WaitForTimeoutAsync(3000);

                // Единственный JS-вызов: __INITIAL_STATE__ → DOM fallback, без локаторов/таймаутов
                var extractedJson = await page.EvaluateAsync<string?>(@"() => {
                    let name = null, price = null, image = null;

                    // 1. __INITIAL_STATE__
                    try {
                        const st = window.__INITIAL_STATE__;
                        const p = st?.productCard?.data?.products?.[0];
                        if (p) {
                            name = p.name || null;
                            price = p.salePriceU ?? p.priceU ?? null;
                            image = p.photos?.[0]?.big || p.photos?.[0]?.url || p.images?.[0]?.big || p.images?.[0]?.url || null;
                        }
                    } catch {}

                    // 2. DOM fallback для имени
                    if (!name) {
                        const h2 = document.querySelector('h2[class*=""productTitle""]');
                        if (h2) {
                            const txt = h2.textContent?.trim();
                            if (txt && !txt.includes('рейтинг')) name = txt;
                        }
                    }
                    if (!name) {
                        const og = document.querySelector('meta[property=""og:title""]');
                        const ogTxt = og?.content?.trim();
                        if (ogTxt && !ogTxt.includes('Wildberries')) name = ogTxt;
                    }

                    // 3. DOM fallback для цены (копейки → рубли)
                    if (price === null) {
                        const priceSelectors = [
                            'h3.mo-typography_variant_title3',
                            'ins[class*=""priceBlockFinalPrice""]',
                            'span[class*=""priceBlockPrice""]',
                            'div[class*=""priceBlockPrice""] span',
                            'span[class*=""price-block__""]',
                            'div[class*=""price-block__final""]',
                            '[data-link=""text{:product^price}""]',
                        ];
                        for (const sel of priceSelectors) {
                            const el = document.querySelector(sel);
                            if (el?.textContent?.trim()) {
                                const m = el.textContent.match(/(\d[\d\s\u00A0\u2009]*)/);
                                if (m) { price = m[1].replace(/[\s\u00A0\u2009]/g, ''); break; }
                            }
                        }
                    }
                    if (price === null) {
                        const meta = document.querySelector('meta[property=""product:price:amount""]');
                        if (meta?.content) price = parseFloat(meta.content) * 100;
                    }
                    if (price === null) {
                        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
                        let node;
                        while ((node = walker.nextNode())) {
                            const t = node.textContent;
                            if (t && /\d[\d\s\u00A0\u2009]*₽/.test(t)) {
                                const m = t.match(/(\d[\d\s\u00A0\u2009]*)/);
                                if (m) { price = m[1].replace(/[\s\u00A0\u2009]/g, ''); break; }
                            }
                        }
                    }

                    // 4. DOM fallback для картинки — ищем big версию
                    if (!image) {
                        // Сначала ищем картинку с /big/ в src
                        const imgs = document.querySelectorAll('img');
                        for (const img of imgs) {
                            const src = img.src || img.getAttribute('data-src-pb') || img.getAttribute('data-src');
                            if (src && src.includes('/big/')) { image = src; break; }
                        }
                    }
                    if (!image) {
                        // Берём любую картинку с basket и меняем tm на big
                        const imgs = document.querySelectorAll('img');
                        for (const img of imgs) {
                            const src = img.src || img.getAttribute('data-src-pb') || img.getAttribute('data-src');
                            if (src && src.includes('basket') && src.includes('/tm/')) {
                                image = src.replace('/tm/', '/big/');
                                break;
                            }
                        }
                    }
                    if (!image) {
                        const og = document.querySelector('meta[property=""og:image""]');
                        image = og?.content || null;
                    }

                    return JSON.stringify({ name, price, image });
                }");

                string? name = null;
                decimal? price = null;
                string? imageUrl = null;

                if (!string.IsNullOrWhiteSpace(extractedJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(extractedJson);
                        var root = doc.RootElement;
                        name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        name = name?.Trim();

                        if (root.TryGetProperty("price", out var pProp))
                        {
                            if (pProp.ValueKind == JsonValueKind.Number)
                            {
                                var priceK = pProp.GetDecimal();
                                price = priceK >= 100 ? priceK / 100m : priceK;
                            }
                            else if (pProp.ValueKind == JsonValueKind.String)
                            {
                                price = ParsePrice(pProp.GetString());
                            }
                        }

                        imageUrl = root.TryGetProperty("image", out var iProp) ? iProp.GetString() : null;
                        imageUrl = NormalizeImageUrl(imageUrl);
                    }
                    catch
                    {
                        // ignore parse errors
                    }
                }

                // Fallback: extract from page DOM if JS didn't provide data
                var nmId = ExtractNmId(url);
                if (string.IsNullOrEmpty(name))
                {
                    // Берём имя товара из разных селекторов, пропускаем если это рейтинг
                    name = await page.EvaluateAsync<string?>(@"() => {
                        // Пробуем h2 с productTitle (основной селектор)
                        const h2 = document.querySelector('h2[class*=""productTitle""]');
                        if (h2) {
                            const txt = h2.textContent?.trim();
                            if (txt && !txt.includes('рейтинг') && !txt.includes('оценка')) return txt;
                        }
                        // Пробуем span с именем товара
                        const spans = document.querySelectorAll('span');
                        for (const s of spans) {
                            const cls = s.className || '';
                            if (cls.includes('productName') || cls.includes('productTitle')) {
                                const txt = s.textContent?.trim();
                                if (txt && txt.length > 5 && !txt.includes('рейтинг')) return txt;
                            }
                        }
                        // Пробуем h1 только если он длинный и без цифр рейтинга
                        const h1 = document.querySelector('h1');
                        if (h1) {
                            const txt = h1.textContent?.trim();
                            // Пропускаем если выглядит как рейтинг (короткий или содержит только цифры и точку)
                            if (txt && !/^\d[\d\s\.]*$/.test(txt) && !txt.includes('рейтинг')) return txt;
                        }
                        return null;
                    }");
                }

                if (!price.HasValue)
                {
                    // Цена из title как fallback
                    var title = await page.TitleAsync();
                    var priceMatch = Regex.Match(title, @"за\s*([\d\s\u00A0]+)\s*₽");
                    if (priceMatch.Success)
                    {
                        var priceStr = priceMatch.Groups[1].Value.Replace("\u00A0", "").Replace(" ", "");
                        if (decimal.TryParse(priceStr, out var priceFromTitle))
                            price = priceFromTitle;
                    }
                }

                // Fallback: construct image URL from nmId
                if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(nmId))
                {
                    if (long.TryParse(nmId, out var nmIdNum))
                    {
                        var vol = nmIdNum / 100000;
                        var part = nmIdNum / 1000;
                        // Пробуем извлечь basket-номер из страницы
                        var basketFromPage = await page.EvaluateAsync<string?>(
                            @"() => { const img = document.querySelector('img[src*=""basket-""]'); const m = img?.src?.match(/basket-(\d+)/); return m ? m[1] : null; }");
                        var basketNum = basketFromPage ?? "01";
                        imageUrl = $"https://basket-{basketNum}.wbbasket.ru/vol{vol}/part{part}/{nmId}/images/big/1.webp";
                    }
                }

                var success = !string.IsNullOrEmpty(name) && price.HasValue;
                return new ParseResult
                {
                    Success = success,
                    Name = name,
                    ImageUrl = imageUrl,
                    Price = price,
                    Currency = "RUB",
                    Source = ProductSource.Wildberries,
                    ErrorMessage = success ? null : $"Missing data: name='{name}', price={price}"
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
                Source = ProductSource.Wildberries
            };
        }
    }

    private static string? ExtractNmId(string url)
    {
        // Prefer /catalog/12345678/... pattern
        var match = Regex.Match(url, @"(?:^|/)catalog/(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // Fallback: first long number in the URL
        match = Regex.Match(url, @"\b(\d{6,})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = Regex.Replace(text, @"[^\d,\.]", "").Replace(" ", "").Replace(",", ".");
        return decimal.TryParse(cleaned, out var price) ? price : null;
    }

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("//")) return $"https:{url}";
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        return url.StartsWith("/") ? $"https://www.wildberries.ru{url}" : url;
    }
}
