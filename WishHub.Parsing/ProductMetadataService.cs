using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using WishHub.Core.DTOs;
using WishHub.Core.Interfaces;

namespace WishHub.Parsing;

public sealed partial class ProductMetadataService(HttpClient http, bool enableOzonWbray = false)
    : IProductMetadataService, IDisposable
{
    public const int MaxResponseBytes = 1024 * 1024;
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    public async Task<ProductMetadataDto> ResolveAsync(string url, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryProduct(url, out var uri, out var source, out var sku))
            return Failure("unsupported", source, "unsupported_url");
        if (source == "ozon" && !enableOzonWbray)
            return Failure("unavailable", source, "source_disabled");
        if (!await _concurrency.WaitAsync(0, ct))
            return Failure("unavailable", source, "busy");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Deadline);
        try
        {
            return source == "wildberries"
                ? await ResolveWildberriesAsync(sku, timeout.Token)
                : await ResolveOzonAsync(uri!, sku, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("unavailable", source, "timeout");
        }
        catch (MetadataException e)
        {
            return Failure("unavailable", source, e.Message);
        }
        catch (JsonException)
        {
            return Failure("unavailable", source, "invalid_response");
        }
        catch (HttpRequestException)
        {
            return Failure("unavailable", source, "source_unavailable");
        }
        catch (IOException)
        {
            return Failure("unavailable", source, "source_unavailable");
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task<ProductMetadataDto> ResolveWildberriesAsync(long sku, CancellationToken ct)
    {
        using var routeRequest = new HttpRequestMessage(HttpMethod.Get, "https://cdn.wbbasket.ru/api/v3/upstreams");
        using var routes = await ReadJsonAsync(routeRequest, ct);
        var maps = Property(Property(routes.RootElement, "recommend"), "mediabasket_route_map");
        if (maps.ValueKind != JsonValueKind.Array || maps.GetArrayLength() == 0)
            throw new MetadataException("unsupported_route_map");
        var route = maps[0];
        var hosts = Property(route, "hosts");
        if (hosts.ValueKind != JsonValueKind.Array || hosts.GetArrayLength() is 0 or > 256)
            throw new MetadataException("unsupported_route_map");

        if (hosts.EnumerateArray().Any(candidate => Text(candidate, "host") is not { } candidateHost ||
            !MetadataHttpTransport.IsBasketHost(candidateHost)))
            throw new MetadataException("unsupported_route_map");

        string? host = null;
        switch (Text(route, "method"))
        {
            case "mod":
                host = Text(hosts[(int)(sku % hosts.GetArrayLength())], "host");
                break;
            case "range":
                foreach (var candidate in hosts.EnumerateArray())
                {
                    var from = Number(candidate, "vol_range_from");
                    var to = Number(candidate, "vol_range_to");
                    if (from is null or < 0 || to is null || to < from)
                        throw new MetadataException("unsupported_route_map");
                    if (sku / 100000 >= from && sku / 100000 <= to)
                    {
                        if (host is not null) throw new MetadataException("unsupported_route_map");
                        host = Text(candidate, "host");
                    }
                }
                break;
            default:
                throw new MetadataException("unsupported_route_map");
        }

        if (host is null || !MetadataHttpTransport.IsBasketHost(host))
            throw new MetadataException("unsupported_route_map");
        var baseUrl = FormattableString.Invariant($"https://{host}/vol{sku / 100000}/part{sku / 1000}/{sku}");
        using var cardRequest = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/info/ru/card.json");
        using var card = await ReadJsonAsync(cardRequest, ct);
        if (Number(card.RootElement, "nm_id") != sku)
            throw new MetadataException("product_mismatch");
        var name = ValidTitle(Text(card.RootElement, "imt_name"));
        var image = Number(Property(card.RootElement, "media"), "photo_count") > 0
            ? baseUrl + "/images/big/1.webp" : null;
        return Result("wildberries", name, image);
    }

    private async Task<ProductMetadataDto> ResolveOzonAsync(Uri uri, long sku, CancellationToken ct)
    {
        // Experimental public website endpoint; opt in only after deployment-side qualification.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://wbray.ru/api/ozon/analyze/")
        {
            Content = JsonContent.Create(new { card = uri.AbsoluteUri, client_id = "", with_video = false })
        };
        using var json = await ReadJsonAsync(request, ct);
        if (Text(json.RootElement, "status") != "success")
            throw new MetadataException("source_unavailable");
        var card = Property(json.RootElement, "card");
        if (Number(card, "sku") != sku) throw new MetadataException("product_mismatch");
        var cardUrl = Text(card, "card_url");
        if (cardUrl is not null &&
            (!TryProduct(cardUrl, out _, out var source, out var returnedSku) || source != "ozon" || returnedSku != sku))
            throw new MetadataException("product_mismatch");

        var name = ValidTitle(Text(card, "title"));
        var image = OzonImage(Text(card, "main_photo"));
        var candidates = Property(card, "photos");
        if (image is null && candidates.ValueKind == JsonValueKind.Array)
        {
            // The main photo counts as the first of at most five examined candidates.
            foreach (var candidate in candidates.EnumerateArray().Take(4))
            {
                image = OzonImage(Text(candidate, "source_url"));
                if (image is not null) break;
            }
        }
        return Result("ozon", name, image);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new MetadataException("rate_limited");
        if ((int)response.StatusCode is >= 300 and < 400) throw new MetadataException("redirect_rejected");
        if (!response.IsSuccessStatusCode) throw new MetadataException("source_unavailable");
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new MetadataException("response_too_large");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxResponseBytes + 1 - (int)buffer.Length)), ct);
            if (count == 0) break;
            if (buffer.Length + count > MaxResponseBytes) throw new MetadataException("response_too_large");
            buffer.Write(chunk, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static bool TryProduct(string? url, out Uri? uri, out string source, out long sku)
    {
        source = "unknown";
        sku = 0;
        uri = null;
        url = url?.Trim();
        if (url is null || url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
            uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            return false;
        Match match;
        switch (uri.IdnHost)
        {
            case "wildberries.ru":
            case "www.wildberries.ru":
                source = "wildberries";
                match = WildberriesPath().Match(uri.AbsolutePath);
                break;
            case "ozon.ru":
            case "www.ozon.ru":
                source = "ozon";
                match = OzonPath().Match(uri.AbsolutePath);
                break;
            case "market.yandex.ru":
                source = "yandexMarket";
                return false;
            default:
                return false;
        }
        return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sku) && sku > 0;
    }

    private static string? OzonImage(string? value) =>
        value is not null && value.Length <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.IdnHost == "ir.ozone.ru" && OzonPhotoPath().IsMatch(uri.AbsolutePath) ? uri.AbsoluteUri : null;

    private static string? ValidTitle(string? value)
    {
        value = value?.Trim();
        if (value is not null && (value.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("капча", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("доступ ограничен", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ozon", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("wildberries", StringComparison.OrdinalIgnoreCase)))
            throw new MetadataException("source_unavailable");
        if (string.IsNullOrEmpty(value) || value.Length > 500 || value.Any(char.IsControl) ||
            value.Contains('<') || value.Contains('>')) return null;
        return value;
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string? Text(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    private static long? Number(JsonElement element, string name)
    {
        var value = Property(element, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None,
            CultureInfo.InvariantCulture, out number) ? number : null;
    }
    private static ProductMetadataDto Result(string source, string? name, string? image) =>
        new(name is not null && image is not null ? "ok" : name is not null || image is not null ? "partial" : "unavailable",
            name, image, source, name is not null && image is not null ? null : "missing_fields");
    private static ProductMetadataDto Failure(string status, string source, string reason) => new(status, null, null, source, reason);
    private sealed class MetadataException(string reason) : Exception(reason);

    [GeneratedRegex(@"^/catalog/([0-9]{1,19})/detail\.aspx$", RegexOptions.CultureInvariant)]
    private static partial Regex WildberriesPath();
    [GeneratedRegex(@"^/product/(?:[a-zA-Z0-9_%-]+-)?([0-9]{1,19})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex OzonPath();

    [GeneratedRegex(@"^/s3/multimedia(?:-[a-zA-Z0-9_-]+)?/[0-9]+\.(?:jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OzonPhotoPath();

    public void Dispose()
    {
        _concurrency.Dispose();
        http.Dispose();
    }
}
