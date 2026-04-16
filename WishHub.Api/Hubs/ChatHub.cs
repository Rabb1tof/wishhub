using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IMessageService messageService, ILogger<ChatHub> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    public async Task SendMessage(Guid receiverId, string content)
    {
        try
        {
            var senderId = GetUserId();
            var message = await _messageService.SendMessageAsync(senderId, receiverId, content);

            // Send to receiver if online
            await Clients.User(receiverId.ToString()).SendAsync("ReceiveMessage", new
            {
                message.Id,
                message.SenderId,
                message.ReceiverId,
                message.Content,
                message.CreatedAt
            });

            // Confirm to sender
            await Clients.Caller.SendAsync("MessageSent", message);

            _logger.LogInformation("Message sent from {SenderId} to {ReceiverId}", senderId, receiverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            throw;
        }
    }

    public async Task MarkAsRead(Guid messageId)
    {
        var userId = GetUserId();
        await _messageService.MarkAsReadAsync(messageId, userId);

        // Notify sender that message was read
        await Clients.Others.SendAsync("MessageRead", new { MessageId = messageId, ReaderId = userId });
    }

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("User {UserId} joined group {GroupName}", GetUserId(), groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("User {UserId} left group {GroupName}", GetUserId(), groupName);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        _logger.LogInformation("User {UserId} connected to ChatHub", userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        _logger.LogInformation("User {UserId} disconnected from ChatHub", userId);
        await base.OnDisconnectedAsync(exception);
    }
}
