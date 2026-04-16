namespace WishHub.Core.Entities;

public class UserExternalLogin
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;  // "vk" | "telegram"
    public string ExternalId { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public DateTime LinkedAt { get; set; }

    public User User { get; set; } = null!;
}
