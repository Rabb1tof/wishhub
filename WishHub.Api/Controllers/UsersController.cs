using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return userIdClaim != null ? Guid.Parse(userIdClaim) : null;
    }

    [HttpGet("{username}")]
    public async Task<ActionResult<UserProfileDto>> GetProfile(string username)
    {
        var viewerId = GetCurrentUserId();
        var profile = await _userService.GetProfileAsync(username, viewerId);

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMyProfile()
    {
        var userId = GetCurrentUserId()!;
        var profile = await _userService.GetProfileByIdAsync(userId.Value, userId);

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMyProfile(UpdateProfileRequest request)
    {
        var userId = GetCurrentUserId()!;
        var profile = await _userService.UpdateProfileAsync(userId.Value, request);

        if (profile == null)
            return BadRequest(new { error = "Failed to update profile" });

        return Ok(profile);
    }

    [Authorize]
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId()!;
        var success = await _userService.ChangePasswordAsync(userId.Value, request);

        if (!success)
            return BadRequest(new { error = "Failed to change password" });

        return NoContent();
    }

    [Authorize]
    [HttpPost("me/avatar")]
    public async Task<ActionResult<object>> UploadAvatar(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        var userId = GetCurrentUserId()!;
        var avatarUrl = await _userService.UploadAvatarAsync(userId.Value, file.OpenReadStream(), file.ContentType, file.FileName);

        if (avatarUrl == null)
            return BadRequest(new { error = "Failed to upload avatar" });

        return Ok(new { avatarUrl });
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<UserProfileDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { error = "Query must be at least 2 characters" });

        var viewerId = GetCurrentUserId();
        var results = await _userService.SearchUsersAsync(q, viewerId);

        return Ok(results);
    }
}
