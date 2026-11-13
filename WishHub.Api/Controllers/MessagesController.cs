using System.ComponentModel.DataAnnotations;
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

    public MessagesController(IMessageService messageService, IUserService userService, IHubContext<ChatHub> hubContext)
    {
        _messageService = messageService;
        _userService = userService;
        _hubContext = hubContext;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    [HttpGet("dialogs")]
    public async Task<ActionResult<IReadOnlyList<DialogDto>>> GetDialogs(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var dialogs = await _messageService.GetDialogsAsync(userId, ct);
        return Ok(dialogs);
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetMessages(
        Guid userId, [FromQuery, Range(1, 10_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, [FromQuery] Guid? before = null,
        CancellationToken ct = default)
    {
        if (before.HasValue && page != 1)
            return BadRequest();

        var result = await _messageService.GetMessagesAsync(GetCurrentUserId(), userId, page, pageSize, before, ct);
        if (result is null)
            return NotFound();

        if (result.NextBefore is { } next)
            Response.Headers["X-Next-Before"] = next.ToString();

        return Ok(result.Messages);
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
        await _hubContext.Clients.User(senderId.ToString()).SendAsync("MessageSent", message);

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

    [HttpPost("{userId:guid}/read-batch")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> MarkMessagesAsRead(
        Guid userId, MarkMessagesReadRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        var (accepted, changed) = await _messageService.MarkMessagesAsReadAsync(currentUserId, userId, request.MessageIds, ct);
        if (changed)
        {
            await _hubContext.Clients.User(userId.ToString()).SendAsync("MessageRead", new
            {
                ReaderId = currentUserId,
                MessageIds = accepted
            });
        }

        return Ok(accepted);
    }

    [HttpGet("unread/count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _messageService.GetUnreadCountAsync(userId);
        return Ok(count);
    }
}
