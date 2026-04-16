namespace WishHub.Core.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Payload { get; set; } = "{}";  // JSON
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}

public enum NotificationType
{
    PriceChanged,
    FriendRequest,
    FriendRequestAccepted,
    ItemReserved,
    NewMessage
}
