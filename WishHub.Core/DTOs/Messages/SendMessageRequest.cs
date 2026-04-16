using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Messages;

public class SendMessageRequest
{
    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;
}
