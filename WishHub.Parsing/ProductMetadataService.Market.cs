using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WishHub.Core.DTOs;

namespace WishHub.Parsing;

public sealed partial class ProductMetadataService
{
    private const string MarketSource = "yandexMarket";
    private const int MarketCatalogBytes = 2 * 1024 * 1024;
    private const string CommoCatalog = "https://commo.yandex.ru/collections/besprovodnye-naushniki";
    private const string DreameCatalog = "https://price.ru/pylesosy/xiaomi-dreame-f21/";

    private static bool IsMarketLink(Uri uri) =>
        uri.IdnHost == "market.yandex.ru" && (MarketCardPath().IsMatch(uri.AbsolutePath) || MarketShortPath().IsMatch(uri.AbsolutePath)) ||
        uri.IdnHost == "ya.cc" && MarketAliasPath().IsMatch(uri.AbsolutePath);

    private async Task<ProductMetadataDto> ResolveMarketAsync(Uri uri, CancellationToken ct)
    {
        if (uri.IdnHost == "ya.cc" || MarketShortPath().IsMatch(uri.AbsolutePath))
            uri = await ResolveMarketShareAsync(uri, ct);
        var card = MarketCardPath().Match(uri.AbsolutePath);
        var (name, image) = await FindMarketVariantAsync(card.Groups[1].Value.ToLowerInvariant(), ct);
        if (name is null && image is null)
            (name, image) = await FindMarketShareDataAsync(uri.AbsolutePath, card.Groups[2].Value, ct);
        return Result(MarketSource, name, image);
    }

    private async Task<Uri> ResolveMarketShareAsync(Uri share, CancellationToken ct)
    {
        var endpoint = new Uri($"https://{share.IdnHost}{share.AbsolutePath}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new MetadataException("rate_limited");
        if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
            HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
            throw new MetadataException("source_unavailable");
        // Only the redirect headers are used. The product page is not requested.
        if (response.Headers.Location is not { } location || !Uri.TryCreate(endpoint, location, out var card) ||
            card.Scheme != Uri.UriSchemeHttps || !card.IsDefaultPort || card.UserInfo.Length != 0 ||
            card.IdnHost != "market.yandex.ru" || card.AbsoluteUri.Length > 8192 || !MarketCardPath().IsMatch(card.AbsolutePath))
            throw new MetadataException("redirect_rejected");
        return card;
    }

    private async Task<(string? Name, string? Image)> FindMarketVariantAsync(string slug, CancellationToken ct)
    {
        // shortcut: match the two families qualified on the preview, expand after verifying a catalog and its variants.
        if (CommoModel().IsMatch(slug))
        {
            var colors = new[] { ("lavandov", "лавандовый"), ("molochn", "молочный"), ("grafitov", "графитовый") };
            var words = slug.Split('-');
            var matches = colors.Where(color => words.Any(word => word.StartsWith(color.Item1, StringComparison.Ordinal))).ToArray();
            if (matches.Length != 1) return (null, null);
            return FindCommo(await ReadMarketCatalogAsync(CommoCatalog, ct), matches[0].Item2);
        }

        if (DreameModel().IsMatch(slug))
        {
            var words = slug.Split('-');
            var black = words.Intersect(new[] { "black", "chernyy", "chernyi", "chornyy" }).Any();
            var white = words.Intersect(new[] { "white", "belyy", "belyi" }).Any();
            if (black == white || words.Any(word => word is "plus" or "pro" or "ultra" or "max"))
                return (null, null);
            return MarketDreameCatalog.Find(await ReadMarketCatalogAsync(DreameCatalog, ct), black ? "black" : "white");
        }
        return (null, null);
    }

    private async Task<(string? Name, string? Image)> FindMarketShareDataAsync(string cardPath, string osku, CancellationToken ct)
    {
        // Public Yandex Go storefront. Use the validated card path only, without referral or offer parameters.
        var html = await ReadMarketCatalogAsync("https://market.yandex.uz/ru" + cardPath, ct);
        foreach (Match block in MarketPagePatch().Matches(html))
        {
            var json = block.Groups[1].Value;
            if (json.Length > 128 * 1024 || !json.Contains("\"@card/ShortUrl\"", StringComparison.Ordinal)) continue;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var collections = Property(document.RootElement, "collections");
            var share = Property(collections, "share");
            if (share.ValueKind != JsonValueKind.Object) continue;
            var compose = Property(collections, "compose");
            foreach (var offer in share.EnumerateObject())
            {
                var context = Property(offer.Value, "context");
                if (Text(context, "type") != "ITEM" || Text(context, "osku") != osku ||
                    Text(Property(compose, offer.Name), "oskuId") != osku)
                    continue;
                var name = ValidTitle(Text(offer.Value, "title"));
                var photo = Text(offer.Value, "picture");
                if (name is not null && photo is { Length: <= 2000 } && Uri.TryCreate(photo, UriKind.Absolute, out var image) &&
                    image.Scheme == Uri.UriSchemeHttps && image.IsDefaultPort && image.UserInfo.Length == 0 &&
                    image.IdnHost == "avatars.mds.yandex.net" && MarketPhotoPath().IsMatch(image.AbsolutePath))
                    return (name, image.GetLeftPart(UriPartial.Path));
            }
        }
        return (null, null);
    }

    private async Task<string> ReadMarketCatalogAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");
        var body = await ReadBodyAsync(request, MarketCatalogBytes, ct);
        return Encoding.UTF8.GetString(body.Span);
    }

    private static (string? Name, string? Image) FindCommo(string html, string color)
    {
        foreach (Match block in JsonLdScript().Matches(html))
        {
            var json = block.Groups[1].Value;
            if (json.Length > 64 * 1024) continue;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var found = FindCommoOffer(document.RootElement, color);
            if (found.Name is not null) return found;
        }
        return (null, null);
    }

    private static (string? Name, string? Image) FindCommoOffer(JsonElement node, string color)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (Text(node, "@type") == "Offer" && Text(node, "name") is { } title && CommoTitle().IsMatch(title) &&
                title.EndsWith(", " + color, StringComparison.OrdinalIgnoreCase) && ValidTitle(title) is { } name &&
                Text(node, "image") is { Length: <= 2000 } image && Uri.TryCreate(image, UriKind.Absolute, out var photo) &&
                photo.Scheme == Uri.UriSchemeHttps && photo.IsDefaultPort && photo.UserInfo.Length == 0 &&
                photo.IdnHost == "avatars.mds.yandex.net" && CommoPhotoPath().IsMatch(photo.AbsolutePath))
                return (name, photo.AbsoluteUri);

            foreach (var property in node.EnumerateObject())
            {
                var found = FindCommoOffer(property.Value, color);
                if (found.Name is not null) return found;
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                var found = FindCommoOffer(item, color);
                if (found.Name is not null) return found;
            }
        }
        return (null, null);
    }

    [GeneratedRegex(@"^/card/([a-zA-Z0-9-]{1,600})/([1-9][0-9]{0,18})/?$")]
    private static partial Regex MarketCardPath();
    [GeneratedRegex(@"^/cc/[A-Za-z0-9_-]{1,64}/?$")]
    private static partial Regex MarketShortPath();
    [GeneratedRegex(@"^/m/[A-Za-z0-9_-]{1,64}/?$")]
    private static partial Regex MarketAliasPath();
    [GeneratedRegex(@"^(?:besprovodnyye-)?naushniki-commo-etude-(?:s-|anc-)(?:[a-z0-9-]*-)?(?:lavandov|molochn|grafitov)", RegexOptions.CultureInvariant)]
    private static partial Regex CommoModel();
    [GeneratedRegex(@"^(?:robot-pylesos|pylesos-robot)-dreame-(?:bot-|robot-vacuum-)?f21-", RegexOptions.CultureInvariant)]
    private static partial Regex DreameModel();
    [GeneratedRegex(@"<script\b[^>]*\btype\s*=\s*['""]application/ld\+json['""][^>]*>(.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex JsonLdScript();
    [GeneratedRegex(@"\bCOMMO Etude с функцией адаптивного шумоподавления ANC,", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommoTitle();
    [GeneratedRegex(@"^/get-yastore/[0-9]+/[a-zA-Z0-9_-]+/orig$")]
    private static partial Regex CommoPhotoPath();
    [GeneratedRegex(@"<noframes\s+data-apiary=['""]patch['""]>(.*?)</noframes>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MarketPagePatch();
    [GeneratedRegex(@"^/get-mpic/[0-9]+/[a-zA-Z0-9_.-]+/orig$")]
    private static partial Regex MarketPhotoPath();
}
