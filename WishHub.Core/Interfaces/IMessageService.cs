using WishHub.Core.DTOs.Messages;

namespace WishHub.Core.Interfaces;

public interface IMessageService
{
    Task<MessageDto> SendMessageAsync(Guid senderId, Guid receiverId, string content, CancellationToken ct = default);
    Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid userId, Guid otherUserId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<IReadOnlyList<DialogDto>> GetDialogsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(Guid messageId, Guid userId, CancellationToken ct = default);
    Task<int> MarkDialogAsReadAsync(Guid userId, Guid otherUserId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
}
