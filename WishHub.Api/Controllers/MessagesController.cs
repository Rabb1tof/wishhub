using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WishHub.Api.Hubs;
using WishHub.Core.DTOs.Messages;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly IUserService _userService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, IUserService userService, IHubContext<ChatHub> hubContext, ILogger<MessagesController> logger)
    {
        _messageService = messageService;
        _userService = userService;
        _hubContext = hubContext;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    [HttpGet("dialogs")]
    public async Task<ActionResult<IReadOnlyList<DialogDto>>> GetDialogs()
    {
        var userId = GetCurrentUserId();
        var dialogs = await _messageService.GetDialogsAsync(userId);
        return Ok(dialogs);
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetMessages(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var currentUserId = GetCurrentUserId();
        var messages = await _messageService.GetMessagesAsync(currentUserId, userId, page, pageSize);
        return Ok(messages);
    }

    [HttpPost("{userId:guid}")]
    public async Task<ActionResult<MessageDto>> SendMessage(Guid userId, SendMessageRequest request)
    {
        var senderId = GetCurrentUserId();

        // Verify receiver exists
        var receiver = await _userService.GetProfileByIdAsync(userId, senderId);
        if (receiver == null)
            return NotFound(new { error = "User not found" });

        var message = await _messageService.SendMessageAsync(senderId, userId, request.Content);

        // Пушим SignalR событие получателю
        await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.SenderId,
            message.ReceiverId,
            message.Content,
            message.CreatedAt
        });

        // Уведомление о новом сообщении
        await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", new
        {
            Type = "new_message",
            MessageId = message.Id,
            SenderId = senderId
        });

        return Ok(message);
    }

    [HttpPost("{messageId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid messageId)
    {
        var userId = GetCurrentUserId();
        var success = await _messageService.MarkAsReadAsync(messageId, userId);

        if (!success)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{userId:guid}/read-all")]
    public async Task<IActionResult> MarkDialogAsRead(Guid userId)
    {
        var currentUserId = GetCurrentUserId();
        var count = await _messageService.MarkDialogAsReadAsync(currentUserId, userId);

        // Уведомляем отправителя, что сообщения прочитаны
        if (count > 0)
        {
            await _hubContext.Clients.User(userId.ToString()).SendAsync("MessageRead", new
            {
                ReaderId = currentUserId
            });
        }

        return NoContent();
    }

    [HttpGet("unread/count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _messageService.GetUnreadCountAsync(userId);
        return Ok(count);
    }
}
