using System.ComponentModel.DataAnnotations;

namespace WishHub.Core.DTOs.Messages;

public class MarkMessagesReadRequest
{
    [Required]
    [Length(1, 100)]
    public Guid[] MessageIds { get; set; } = [];
}
