using System.ComponentModel.DataAnnotations;
using WishHub.Core.Validation;

namespace WishHub.Core.DTOs.Wishlist;

/// <summary>The user-owned values to save without fetching product metadata.</summary>
public class AddWishlistItemRequest : IValidatableObject
{
    [Required, HttpUrl]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [HttpUrl]
    public string? ImageUrl { get; set; }

    public decimal? Price { get; set; }
    public string? Currency { get; set; } = "RUB";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 500 || Name.Contains('\0'))
            yield return new ValidationResult("Name must contain 1 to 500 characters and no NUL characters.", [nameof(Name)]);
        if (Price < 0)
            yield return new ValidationResult("Price cannot be negative.", [nameof(Price)]);
        if (Currency?.Trim().Length > 10 || Currency?.Contains('\0') == true)
            yield return new ValidationResult("Currency must contain at most 10 characters and no NUL characters.", [nameof(Currency)]);
    }
}
