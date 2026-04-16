using WishHub.Core.DTOs.Users;

namespace WishHub.Core.DTOs.Friends;

public class FriendshipDto
{
    public Guid Id { get; set; }
    public UserProfileDto User { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
