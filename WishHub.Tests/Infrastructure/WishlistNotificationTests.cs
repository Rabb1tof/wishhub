using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

public class WishlistNotificationTests
{
    [Fact]
    public async Task GetUnread_LegacyReservations_HidesThemBeforeLimitAndKeepsOtherNotifications()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var legacy = Create(owner.Id, NotificationType.ItemReserved, 5,
            """{"ItemId":"legacy-item","ReservedById":"legacy-reserver"}""");
        var newestVisible = Create(owner.Id, NotificationType.NewMessage, 4, """{"MessageId":"message"}""");
        var olderVisible = Create(owner.Id, NotificationType.PriceChanged, 3, """{"OldPrice":20,"NewPrice":10}""");
        var alreadyRead = Create(owner.Id, NotificationType.FriendRequest, 2);
        alreadyRead.IsRead = true;
        var otherUsersNotification = Create(other.Id, NotificationType.FriendRequest, 6);
        store.Db.Notifications.AddRange(legacy, newestVisible, olderVisible, alreadyRead, otherUsersNotification);
        await store.Db.SaveChangesAsync();
        var service = new NotificationService(store.Db, NullLogger<NotificationService>.Instance);

        var limited = await service.GetUnreadNotificationsAsync(owner.Id, limit: 1);
        var all = await service.GetUnreadNotificationsAsync(owner.Id);
        var count = await service.GetUnreadCountAsync(owner.Id);

        Assert.Equal(newestVisible.Id, Assert.Single(limited).Id);
        Assert.Equal(new[] { newestVisible.Id, olderVisible.Id }, all.Select(n => n.Id));
        Assert.Equal(newestVisible.Payload, all[0].Payload);
        Assert.Equal(olderVisible.Payload, all[1].Payload);
        Assert.Equal(2, count);
        Assert.Equal(otherUsersNotification.Id, Assert.Single(await service.GetUnreadNotificationsAsync(other.Id)).Id);
        store.Db.ChangeTracker.Clear();
        var retained = await store.Db.Notifications.SingleAsync(n => n.Id == legacy.Id);
        Assert.Equal(5, await store.Db.Notifications.CountAsync());
        Assert.Equal(NotificationType.ItemReserved, retained.Type);
        Assert.Equal(legacy.Payload, retained.Payload);
        Assert.False(retained.IsRead);
    }

    [Fact]
    public async Task MarkAllAsRead_LegacyReservations_OnlyCountsAndUpdatesVisibleHistory()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var legacy = Create(owner.Id, NotificationType.ItemReserved, 3, """{"ReservedById":"secret"}""");
        var firstVisible = Create(owner.Id, NotificationType.FriendRequest, 2);
        var secondVisible = Create(owner.Id, NotificationType.NewMessage, 1);
        var otherUsersNotification = Create(other.Id, NotificationType.NewMessage, 4);
        store.Db.Notifications.AddRange(legacy, firstVisible, secondVisible, otherUsersNotification);
        await store.Db.SaveChangesAsync();
        var service = new NotificationService(store.Db, NullLogger<NotificationService>.Instance);

        Assert.Equal(2, await service.MarkAllAsReadAsync(owner.Id));
        Assert.Equal(0, await service.MarkAllAsReadAsync(owner.Id));
        Assert.Equal(0, await service.GetUnreadCountAsync(owner.Id));
        Assert.Empty(await service.GetUnreadNotificationsAsync(owner.Id));

        store.Db.ChangeTracker.Clear();
        var history = await store.Db.Notifications.ToDictionaryAsync(n => n.Id);
        Assert.Equal(4, history.Count);
        Assert.True(history[firstVisible.Id].IsRead);
        Assert.True(history[secondVisible.Id].IsRead);
        Assert.False(history[legacy.Id].IsRead);
        Assert.Equal(legacy.Payload, history[legacy.Id].Payload);
        Assert.False(history[otherUsersNotification.Id].IsRead);
    }

    [Fact]
    public async Task MarkAsRead_HiddenOrOtherUsersNotification_DoesNotRevealOrChangeIt()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var legacy = Create(owner.Id, NotificationType.ItemReserved, 2);
        var visible = Create(owner.Id, NotificationType.FriendRequestAccepted, 1);
        store.Db.Notifications.AddRange(legacy, visible);
        await store.Db.SaveChangesAsync();
        var service = new NotificationService(store.Db, NullLogger<NotificationService>.Instance);

        Assert.False(await service.MarkAsReadAsync(legacy.Id, owner.Id));
        Assert.False(await service.MarkAsReadAsync(visible.Id, other.Id));
        Assert.False(await service.MarkAsReadAsync(Guid.NewGuid(), owner.Id));
        Assert.Equal(1, await service.GetUnreadCountAsync(owner.Id));
        Assert.True(await service.MarkAsReadAsync(visible.Id, owner.Id));
        Assert.Equal(0, await service.GetUnreadCountAsync(owner.Id));

        store.Db.ChangeTracker.Clear();
        Assert.Equal(2, await store.Db.Notifications.CountAsync());
        Assert.False((await store.Db.Notifications.SingleAsync(n => n.Id == legacy.Id)).IsRead);
        Assert.True((await store.Db.Notifications.SingleAsync(n => n.Id == visible.Id)).IsRead);
    }

    private static Notification Create(Guid userId, NotificationType type, int hour, string payload = "{}") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = type,
        Payload = payload,
        CreatedAt = new DateTime(2026, 10, 30, hour, 0, 0, DateTimeKind.Utc)
    };
}
