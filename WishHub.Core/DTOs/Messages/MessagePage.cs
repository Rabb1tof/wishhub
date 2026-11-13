namespace WishHub.Core.DTOs.Messages;

public record MessagePage(IReadOnlyList<MessageDto> Messages, Guid? NextBefore);
