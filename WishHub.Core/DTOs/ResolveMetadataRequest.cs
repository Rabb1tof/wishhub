using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs;

/// <summary>Requests optional metadata without creating or updating a wishlist item.</summary>
public sealed record ResolveMetadataRequest
{
    private string _url = string.Empty;

    [Required, MaxLength(2000)]
    public string Url
    {
        get => _url;
        init => _url = value?.Trim() ?? string.Empty;
    }
}
