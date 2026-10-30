namespace WishHub.Core.Validation;

public readonly record struct ManagedWishlistImage(Guid OwnerId, Guid ImageId, string Extension)
{
    public const string Prefix = "/api/wishlist-images/";
    public string Url => $"{Prefix}{OwnerId:D}/{ImageId:D}.{Extension}";
    public string ContentType => Extension switch
    {
        "jpg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => throw new InvalidOperationException("Unsupported wishlist image extension.")
    };

    public static bool TryParse(string? value, out ManagedWishlistImage image)
    {
        image = default;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var parts = value[Prefix.Length..].Split('/');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out var ownerId) || ownerId == Guid.Empty)
            return false;
        var dot = parts[1].LastIndexOf('.');
        if (dot != 36 || !Guid.TryParseExact(parts[1][..dot], "D", out var imageId) || imageId == Guid.Empty)
            return false;
        var extension = parts[1][(dot + 1)..];
        if (extension is not ("jpg" or "png" or "webp"))
            return false;
        image = new(ownerId, imageId, extension);
        return value == image.Url;
    }
}

public static class WishlistImageLimits
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const long MaxOwnerBytes = 100 * 1024 * 1024;
}
