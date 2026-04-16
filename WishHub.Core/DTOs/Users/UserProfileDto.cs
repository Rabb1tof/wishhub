using WishHub.Core.Entities;

namespace WishHub.Core.DTOs.Users;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public string WishlistPrivacy { get; set; } = "Public";
    public FriendshipStatus? FriendshipStatus { get; set; }
    public int WishlistItemCount { get; set; }
    public int PriceRefreshIntervalHours { get; set; } = 24;
}
