using WishHub.Core.Entities;

namespace WishHub.Core.Interfaces;

public interface INotificationService
{
    Task<IReadOnlyList<Notification>> GetUnreadNotificationsAsync(Guid userId, int limit = 50, CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);
}
