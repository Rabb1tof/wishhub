using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WishHub.Core.DTOs;
using WishHub.Core.Interfaces;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/metadata")]
[Authorize]
[EnableRateLimiting("metadata")]
public sealed class MetadataController(IProductMetadataService metadata) : ControllerBase
{
    [HttpPost("resolve")]
    [ProducesResponseType<ProductMetadataDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProductMetadataDto>> Resolve(ResolveMetadataRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0)
            return BadRequest(new { message = "URL must be an absolute HTTP(S) URL without credentials." });
        return Ok(await metadata.ResolveAsync(request.Url.Trim(), ct));
    }
}
