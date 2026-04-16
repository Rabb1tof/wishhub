using WishHub.Core.DTOs.Users;

namespace WishHub.Core.DTOs.Messages;

public class DialogDto
{
    public UserProfileDto User { get; set; } = null!;
    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }
}
