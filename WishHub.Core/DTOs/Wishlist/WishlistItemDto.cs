namespace WishHub.Core.DTOs.Wishlist;

public class WishlistItemDto
{
    public Guid Id { get; set; }
    public string? CustomName { get; set; }
    public bool IsReserved { get; set; }
    public bool IsReservedByMe { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public ProductDto Product { get; set; } = null!;
    public UserDto? ReservedBy { get; set; }
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}
