using WishHub.Core.DTOs.Messages;

namespace WishHub.Core.Interfaces;

public interface IMessageService
{
    Task<MessageDto> SendMessageAsync(Guid senderId, Guid receiverId, string content, CancellationToken ct = default);
    Task<MessagePage?> GetMessagesAsync(Guid userId, Guid otherUserId, int page, int pageSize, Guid? before, CancellationToken ct = default);
    Task<IReadOnlyList<DialogDto>> GetDialogsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(Guid messageId, Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<Guid> Accepted, bool Changed)> MarkMessagesAsReadAsync(Guid userId, Guid otherUserId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
}
