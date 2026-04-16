using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WishHub.Core.DTOs.Messages;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class MessageService : IMessageService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserService _userService;
    private readonly ILogger<MessageService> _logger;

    public MessageService(AppDbContext dbContext, IUserService userService, ILogger<MessageService> logger)
    {
        _dbContext = dbContext;
        _userService = userService;
        _logger = logger;
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

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid userId, Guid otherUserId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var messages = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                        (m.SenderId == otherUserId && m.ReceiverId == userId))
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return messages.Select(MapToDto).Reverse().ToList();
    }

    public async Task<IReadOnlyList<DialogDto>> GetDialogsAsync(Guid userId, CancellationToken ct = default)
    {
        // Get all unique conversation partners
        var partnerIds = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
            .Distinct()
            .ToListAsync(ct);

        var dialogs = new List<DialogDto>();

        foreach (var partnerId in partnerIds)
        {
            var partner = await _userService.GetProfileByIdAsync(partnerId, userId, ct);
            if (partner == null) continue;

            var lastMessage = await _dbContext.Messages
                .AsNoTracking()
                .Where(m => (m.SenderId == userId && m.ReceiverId == partnerId) ||
                            (m.SenderId == partnerId && m.ReceiverId == userId))
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var unreadCount = await _dbContext.Messages
                .AsNoTracking()
                .CountAsync(m => m.SenderId == partnerId && m.ReceiverId == userId && !m.IsRead, ct);

            dialogs.Add(new DialogDto
            {
                User = partner,
                LastMessage = lastMessage != null ? MapToDto(lastMessage) : null,
                UnreadCount = unreadCount
            });
        }

        // Sort by last message date
        return dialogs.OrderByDescending(d => d.LastMessage?.CreatedAt ?? DateTime.MinValue).ToList();
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

    public async Task<int> MarkDialogAsReadAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        var unreadMessages = await _dbContext.Messages
            .Where(m => m.SenderId == otherUserId && m.ReceiverId == userId && !m.IsRead)
            .ToListAsync(ct);

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
        }

        await _dbContext.SaveChangesAsync(ct);
        return unreadMessages.Count;
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
