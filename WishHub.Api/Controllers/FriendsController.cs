using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WishHub.Core.DTOs.Friends;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly IFriendshipService _friendshipService;
    private readonly ILogger<FriendsController> _logger;

    public FriendsController(IFriendshipService friendshipService, ILogger<FriendsController> logger)
    {
        _friendshipService = friendshipService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FriendshipDto>>> GetFriends()
    {
        var userId = GetCurrentUserId();
        var friends = await _friendshipService.GetFriendsAsync(userId);
        return Ok(friends);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetRequests([FromQuery] string type = "incoming")
    {
        var userId = GetCurrentUserId();
        var requests = type.ToLower() switch
        {
            "outgoing" => await _friendshipService.GetOutgoingRequestsAsync(userId),
            _ => await _friendshipService.GetIncomingRequestsAsync(userId)
        };
        return Ok(requests);
    }

    [HttpPost("request/{userId:guid}")]
    public async Task<ActionResult<FriendshipDto>> SendRequest(Guid userId)
    {
        var requesterId = GetCurrentUserId();
        var friendship = await _friendshipService.SendRequestAsync(requesterId, userId);

        if (friendship == null)
            return BadRequest(new { error = "Cannot send friend request" });

        return CreatedAtAction(nameof(GetRequests), new { type = "outgoing" }, friendship);
    }

    [HttpPost("accept/{friendshipId:guid}")]
    public async Task<ActionResult<FriendshipDto>> AcceptRequest(Guid friendshipId)
    {
        var userId = GetCurrentUserId();
        var friendship = await _friendshipService.AcceptRequestAsync(friendshipId, userId);

        if (friendship == null)
            return BadRequest(new { error = "Cannot accept friend request" });

        return Ok(friendship);
    }

    [HttpDelete("request/{friendshipId:guid}")]
    public async Task<IActionResult> RejectOrCancelRequest(Guid friendshipId)
    {
        var userId = GetCurrentUserId();
        var success = await _friendshipService.RejectOrCancelRequestAsync(friendshipId, userId);

        if (!success)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveFriend(Guid userId)
    {
        var currentUserId = GetCurrentUserId();
        var success = await _friendshipService.RemoveFriendAsync(currentUserId, userId);

        if (!success)
            return NotFound();

        return NoContent();
    }
}
