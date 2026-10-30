using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Exceptions;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/wishlist-images")]
public sealed class WishlistImagesController(IWishlistImageService images, ILogger<WishlistImagesController> logger) : ControllerBase
{
    private const long RequestLimit = WishlistImageLimits.MaxFileBytes + 64 * 1024;

    [HttpPost]
    [Authorize]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit, MemoryBufferThreshold = 64 * 1024,
        MultipartHeadersCountLimit = 8, MultipartHeadersLengthLimit = 8192, ValueCountLimit = 1, ValueLengthLimit = 1024)]
    public async Task<ActionResult<UploadWishlistImageResponse>> Upload(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
            return Forbid();
        if (Request.ContentLength > RequestLimit)
            return TooLarge();
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType) ||
            !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Expected multipart/form-data with a file field." });

        try
        {
            var form = await Request.ReadFormAsync(ct);
            if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file")
                return BadRequest(new { error = "A single file field is required." });
            var file = form.Files[0];
            if (file.Length > WishlistImageLimits.MaxFileBytes)
                return TooLarge();
            await using var content = file.OpenReadStream();
            var url = await images.UploadAsync(ownerId, content, file.ContentType, ct);
            return Created(url, new UploadWishlistImageResponse(url));
        }
        catch (WishlistImageUploadException ex)
        {
            var status = ex.Error switch
            {
                WishlistImageUploadError.TooLarge => StatusCodes.Status413PayloadTooLarge,
                WishlistImageUploadError.QuotaExceeded => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, new { error = ex.Message });
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("Multipart body length limit ", StringComparison.Ordinal))
        {
            return TooLarge();
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { error = "Invalid multipart upload." });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Wishlist image storage failed for owner {OwnerId}", ownerId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, detail: "Image storage is unavailable.");
        }
    }

    [HttpGet("{ownerId}/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetImage(string ownerId, string fileName, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (!ManagedWishlistImage.TryParse($"{ManagedWishlistImage.Prefix}{ownerId}/{fileName}", out var image))
            return NotFound();
        Guid? viewerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var stream = await images.OpenReadAsync(image, viewerId, ct);
        return stream is null ? NotFound() : File(stream, image.ContentType);
    }

    private ObjectResult TooLarge() =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "Image exceeds the 5 MiB limit." });
}
