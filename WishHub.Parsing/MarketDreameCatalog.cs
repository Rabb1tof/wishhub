using System.Net;
using System.Text.RegularExpressions;

namespace WishHub.Parsing;

internal static partial class MarketDreameCatalog
{
    internal static (string? Name, string? Image) Find(string html, string color)
    {
        if (color is not ("black" or "white")) return (null, null);

        foreach (Match offer in OfferImage().Matches(html))
        {
            var name = WebUtility.HtmlDecode(offer.Groups["name"].Value);
            var variant = BaseModel().Match(name);
            if (!variant.Success || Color(variant.Groups["color"].Value) != color ||
                variant.Groups["duplicate"].Success && Color(variant.Groups["duplicate"].Value) != color)
                continue;

            var image = WebUtility.HtmlDecode(offer.Groups["image"].Value);
            if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
                uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.IdnHost == "static.price.ru" &&
                uri.Query.Length == 0 && uri.Fragment.Length == 0 && AssetPath().IsMatch(uri.AbsolutePath))
                return (name, uri.AbsoluteUri);
        }

        return (null, null);
    }

    private static string Color(string text) => text.ToLowerInvariant() switch
    {
        "black" or "черный" or "чёрный" => "black",
        "white" or "белый" => "white",
        _ => ""
    };

    // Read the title and image from the same offer block, not the catalog's shared gallery.
    [GeneratedRegex("""<div\s+class="p-c-price__image"[^<>]{0,512}>\s*<meta\s+itemprop="image"\s+content="(?<image>[^"<>]{1,1000})"[^<>]{0,512}>\s*(?:<!---->\s*)?<img\s+alt="(?<name>[^"<>]{1,200})["]""", RegexOptions.CultureInvariant)]
    private static partial Regex OfferImage();

    [GeneratedRegex(@"\A(?:Робот[- ]пылесос|Пылесос)\s+Dreame\s+(?:(?:Bot|Robot Vacuum)\s+)?F21\s+(?<color>black|white|ч[её]рный|белый)(?:\s+\((?<duplicate>black|white|ч[её]рный|белый)\))?(?:\s+\((?:EAC|RLF42GA)\))?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaseModel();

    [GeneratedRegex(@"\A/-/7107-xiaomi-dreame-f21/[0-9a-f]{32}/[^<>]+\.(?:jpe?g|png|webp)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetPath();
}
