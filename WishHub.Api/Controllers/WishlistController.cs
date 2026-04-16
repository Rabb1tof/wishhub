using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;
    private readonly ILogger<WishlistController> _logger;

    public WishlistController(IWishlistService wishlistService, ILogger<WishlistController> logger)
    {
        _wishlistService = wishlistService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WishlistItemDto>>> GetMyWishlist()
    {
        var userId = GetCurrentUserId();
        var items = await _wishlistService.GetItemsAsync(userId, userId);
        return Ok(items);
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyList<WishlistItemDto>>> GetUserWishlist(Guid userId)
    {
        var currentUserId = GetCurrentUserId();

        // Проверяем доступ с учётом приватности
        var hasAccess = await _wishlistService.CanViewWishlistAsync(userId, currentUserId, HttpContext.RequestAborted);
        if (!hasAccess)
            return StatusCode(403, new { error = "Wishlist is private" });

        var items = await _wishlistService.GetItemsAsync(userId, currentUserId);
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<WishlistItemDto>> AddItem(AddWishlistItemRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var item = await _wishlistService.AddItemAsync(userId, request);
            return CreatedAtAction(nameof(GetMyWishlist), new { id = item.Id }, item);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to add wishlist item for URL: {Url}", request.Url);
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WishlistItemDto>> UpdateItem(Guid id, UpdateWishlistItemRequest request)
    {
        var userId = GetCurrentUserId();
        var item = await _wishlistService.UpdateItemAsync(userId, id, request);

        if (item == null)
            return NotFound();

        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id)
    {
        var userId = GetCurrentUserId();
        await _wishlistService.DeleteItemAsync(userId, id);
        return NoContent();
    }

    [HttpPost("{id:guid}/reserve")]
    public async Task<ActionResult<WishlistItemDto>> ReserveItem(Guid id)
    {
        var userId = GetCurrentUserId();
        var item = await _wishlistService.ReserveItemAsync(id, userId);

        if (item == null)
            return BadRequest(new { error = "Cannot reserve this item" });

        return Ok(item);
    }

    [HttpPost("{id:guid}/refresh")]
    public async Task<ActionResult<WishlistItemDto>> RefreshItem(Guid id)
    {
        var userId = GetCurrentUserId();
        try
        {
            var item = await _wishlistService.RefreshItemAsync(id, userId);
            if (item == null)
                return NotFound();
            return Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to refresh wishlist item {ItemId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel-reservation")]
    public async Task<ActionResult<WishlistItemDto>> CancelReservation(Guid id)
    {
        var userId = GetCurrentUserId();
        var item = await _wishlistService.CancelReservationAsync(id, userId);

        if (item == null)
            return BadRequest(new { error = "Cannot cancel reservation" });

        return Ok(item);
    }
}
