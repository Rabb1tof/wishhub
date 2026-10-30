using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Users;

public class UpdateProfileRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [CustomValidation(typeof(UpdateProfileRequest), nameof(ValidateWishlistPrivacy))]
    public string? WishlistPrivacy { get; set; }

    public static ValidationResult? ValidateWishlistPrivacy(string? value) =>
        value is null ||
        string.Equals(value, "Public", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "FriendsOnly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Private", StringComparison.OrdinalIgnoreCase)
            ? ValidationResult.Success
            : new ValidationResult("WishlistPrivacy must be Public, FriendsOnly or Private.");
}
