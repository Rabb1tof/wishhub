using Microsoft.AspNetCore.Identity;

namespace WishHub.Core.Entities;

public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public WishlistPrivacy WishlistPrivacy { get; set; } = WishlistPrivacy.Public;

    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = [];
    public ICollection<Friendship> SentFriendRequests { get; set; } = [];
    public ICollection<Friendship> ReceivedFriendRequests { get; set; } = [];
    public ICollection<Message> SentMessages { get; set; } = [];
    public ICollection<Message> ReceivedMessages { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}

public enum WishlistPrivacy { Public, FriendsOnly, Private }
