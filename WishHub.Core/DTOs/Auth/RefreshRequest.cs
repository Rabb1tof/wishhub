using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Auth;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
