using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WishlistController(IWishlistService wishlistService) : ControllerBase
{
    private Guid GetCurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WishlistItemDto>>> GetMyWishlist(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        return Ok(await wishlistService.GetItemsAsync(userId, userId, ct));
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyList<WishlistItemDto>>> GetUserWishlist(Guid userId, CancellationToken ct)
    {
        var viewerId = GetCurrentUserId();
        if (!await wishlistService.CanViewWishlistAsync(userId, viewerId, ct))
            return StatusCode(403, new { error = "Wishlist is private" });
        return Ok(await wishlistService.GetItemsAsync(userId, viewerId, ct));
    }

    [HttpPost]
    [ProducesResponseType<WishlistItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WishlistItemDto>> AddItem(AddWishlistItemRequest request, CancellationToken ct)
    {
        try
        {
            var item = await wishlistService.AddItemAsync(GetCurrentUserId(), request, ct);
            return CreatedAtAction(nameof(GetMyWishlist), new { id = item.Id }, item);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<WishlistItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WishlistItemDto>> UpdateItem(Guid id, UpdateWishlistItemRequest request, CancellationToken ct)
    {
        try
        {
            var item = await wishlistService.UpdateItemAsync(GetCurrentUserId(), id, request, ct);
            return item == null ? NotFound() : Ok(item);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        await wishlistService.DeleteItemAsync(GetCurrentUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reserve")]
    public async Task<ActionResult<WishlistItemDto>> ReserveItem(Guid id, CancellationToken ct)
    {
        var item = await wishlistService.ReserveItemAsync(id, GetCurrentUserId(), ct);
        return item == null ? BadRequest(new { error = "Cannot reserve this item" }) : Ok(item);
    }

    [HttpPost("{id:guid}/cancel-reservation")]
    public async Task<ActionResult<WishlistItemDto>> CancelReservation(Guid id, CancellationToken ct)
    {
        var item = await wishlistService.CancelReservationAsync(id, GetCurrentUserId(), ct);
        return item == null ? BadRequest(new { error = "Cannot cancel reservation" }) : Ok(item);
    }
}
