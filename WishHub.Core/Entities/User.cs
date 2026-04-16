using Microsoft.AspNetCore.Identity;

namespace WishHub.Core.Entities;

public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public WishlistPrivacy WishlistPrivacy { get; set; } = WishlistPrivacy.Public;
    /// <summary>
    /// Интервал автообновления цен товаров в часах (мин. 1, макс. 168 = 7 дней)
    /// </summary>
    public int PriceRefreshIntervalHours { get; set; } = 24;

    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = [];
    public ICollection<Friendship> SentFriendRequests { get; set; } = [];
    public ICollection<Friendship> ReceivedFriendRequests { get; set; } = [];
    public ICollection<Message> SentMessages { get; set; } = [];
    public ICollection<Message> ReceivedMessages { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}

public enum WishlistPrivacy { Public, FriendsOnly, Private }
