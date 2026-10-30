namespace WishHub.Core.DTOs.Wishlist;

/// <summary>Complete replacement of card values; null image and price clear them.</summary>
public class UpdateWishlistItemRequest : AddWishlistItemRequest
{
    public int? SortOrder { get; set; }
}
