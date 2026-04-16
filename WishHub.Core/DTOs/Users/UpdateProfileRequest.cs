using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Users;

public class UpdateProfileRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    public string? WishlistPrivacy { get; set; } // Public, FriendsOnly, Private

    [Range(1, 168)]
    public int? PriceRefreshIntervalHours { get; set; }
}
