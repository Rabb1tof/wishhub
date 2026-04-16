using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Wishlist;

public class AddWishlistItemRequest
{
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? CustomName { get; set; }
}
