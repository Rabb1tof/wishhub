namespace WishHub.Core.DTOs.Wishlist;

/// <summary>A standalone wishlist snapshot with reservation details hidden from its owner.</summary>
public class WishlistItemDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "RUB";
    public string Source { get; set; } = string.Empty;
    public bool IsReserved { get; set; }
    public bool IsReservedByMe { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserDto? ReservedBy { get; set; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}
