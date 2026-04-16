using WishHub.Core.DTOs.Users;

namespace WishHub.Core.DTOs.Friends;

public class FriendRequestDto
{
    public Guid Id { get; set; }
    public UserProfileDto From { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
