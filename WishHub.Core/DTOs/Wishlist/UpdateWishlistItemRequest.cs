using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Wishlist;

public class UpdateWishlistItemRequest
{
    [MaxLength(500)]
    public string? CustomName { get; set; }

    public int? SortOrder { get; set; }
}
