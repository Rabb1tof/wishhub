using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Messages;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class MessageService : IMessageService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserService _userService;

    public MessageService(AppDbContext dbContext, IUserService userService)
    {
        _dbContext = dbContext;
        _userService = userService;
    }

    public async Task<MessageDto> SendMessageAsync(Guid senderId, Guid receiverId, string content, CancellationToken ct = default)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = content,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Messages.Add(message);

        // Create notification for receiver
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = receiverId,
            Type = NotificationType.NewMessage,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { MessageId = message.Id, SenderId = senderId }),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);

        return MapToDto(message);
    }

    public async Task<MessagePage?> GetMessagesAsync(Guid userId, Guid otherUserId, int page, int pageSize, Guid? before, CancellationToken ct = default)
    {
        var query = _dbContext.Messages
            .AsNoTracking()
            .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                        (m.SenderId == otherUserId && m.ReceiverId == userId));

        if (before is { } cursor)
        {
            var anchor = await query.Where(m => m.Id == cursor)
                .Select(m => new { m.CreatedAt, m.Id })
                .SingleOrDefaultAsync(ct);
            if (anchor is null)
                return null;

            query = query.Where(m => m.CreatedAt < anchor.CreatedAt ||
                (m.CreatedAt == anchor.CreatedAt && m.Id.CompareTo(anchor.Id) < 0));
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip(before is null ? (page - 1) * pageSize : 0)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var nextBefore = messages.Count > pageSize ? messages[pageSize - 1].Id : (Guid?)null;
        if (nextBefore.HasValue)
            messages.RemoveAt(pageSize);

        messages.Reverse();
        return new MessagePage(messages.Select(MapToDto).ToList(), nextBefore);
    }

    public async Task<IReadOnlyList<DialogDto>> GetDialogsAsync(Guid userId, CancellationToken ct = default)
    {
        var summaries = await _dbContext.Messages.AsNoTracking()
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .GroupBy(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
            .Select(g => new
            {
                PartnerId = g.Key,
                LastMessage = g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).First(),
                UnreadCount = g.Count(m => m.ReceiverId == userId && !m.IsRead)
            })
            .ToListAsync(ct);

        var profiles = await _userService.GetProfilesByIdsAsync(summaries.Select(d => d.PartnerId).ToArray(), userId, ct);
        return summaries
            .Where(d => profiles.ContainsKey(d.PartnerId))
            .OrderByDescending(d => d.LastMessage.CreatedAt)
            .ThenByDescending(d => d.LastMessage.Id)
            .Select(d => new DialogDto
            {
                User = profiles[d.PartnerId],
                LastMessage = MapToDto(d.LastMessage),
                UnreadCount = d.UnreadCount
            })
            .ToList();
    }

    public async Task<bool> MarkAsReadAsync(Guid messageId, Guid userId, CancellationToken ct = default)
    {
        var message = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ReceiverId == userId, ct);

        if (message == null)
            return false;

        message.IsRead = true;
        await _dbContext.SaveChangesAsync(ct);

        return true;
    }

    public async Task<(IReadOnlyList<Guid> Accepted, bool Changed)> MarkMessagesAsReadAsync(
        Guid userId, Guid otherUserId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        var matching = _dbContext.Messages.Where(m =>
            m.SenderId == otherUserId && m.ReceiverId == userId && messageIds.Contains(m.Id));
        var accepted = await matching.Select(m => m.Id).ToListAsync(ct);
        if (accepted.Count == 0)
            return (accepted, false);

        var changed = await _dbContext.Messages.Where(m => !m.IsRead && accepted.Contains(m.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsRead, true), ct);
        return (accepted, changed > 0);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        return await _dbContext.Messages
            .AsNoTracking()
            .CountAsync(m => m.ReceiverId == userId && !m.IsRead, ct);
    }

    private static MessageDto MapToDto(Message message)
    {
        return new MessageDto
        {
            Id = message.Id,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            Content = message.Content,
            IsRead = message.IsRead,
            CreatedAt = message.CreatedAt
        };
    }
}
