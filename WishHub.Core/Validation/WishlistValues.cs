using System.ComponentModel.DataAnnotations;
using WishHub.Core.Entities;

namespace WishHub.Core.Validation;

public static class WishlistValues
{
    public static bool TryNormalizeUrl(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 2000 || normalized.Any(char.IsWhiteSpace) ||
            normalized.Any(char.IsControl) || normalized.Contains('\\') ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var authorityStart = normalized.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
            return false;
        var authorityEnd = normalized.IndexOfAny(['/', '?', '#'], authorityStart + 3);
        if (authorityEnd < 0)
            authorityEnd = normalized.Length;
        normalized = normalized[..authorityEnd].ToLowerInvariant() + normalized[authorityEnd..];
        return true;
    }

    public static string NormalizeUrl(string? value)
    {
        if (!TryNormalizeUrl(value, out var normalized))
            throw new ValidationException("URL must be an absolute HTTP(S) URL of at most 2000 characters.");
        return normalized;
    }

    public static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 500 || name.Contains('\0'))
            throw new ValidationException("Name must contain 1 to 500 characters and no NUL characters.");
        return name;
    }

    public static string NormalizeCurrency(string? value)
    {
        var currency = string.IsNullOrWhiteSpace(value) ? "RUB" : value.Trim();
        if (currency.Length > 10 || currency.Contains('\0'))
            throw new ValidationException("Currency must contain at most 10 characters and no NUL characters.");
        return currency;
    }

    public static ProductSource GetSource(string normalizedUrl)
    {
        var host = new Uri(normalizedUrl).IdnHost;
        bool Matches(string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        if (Matches("wildberries.ru") || Matches("wb.ru"))
            return ProductSource.Wildberries;
        if (Matches("ozon.ru") || Matches("ozon.com"))
            return ProductSource.Ozon;
        if (Matches("market.yandex.ru"))
            return ProductSource.YandexMarket;
        return ProductSource.Unknown;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null || value is string url && WishlistValues.TryNormalizeUrl(url, out _);
}
