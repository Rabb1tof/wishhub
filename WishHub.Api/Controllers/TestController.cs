using Microsoft.AspNetCore.Mvc;
using WishHub.Core.Services;

namespace WishHub.Api.Controllers;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly ParserFactory _parserFactory;
    private readonly ILogger<TestController> _logger;

    public TestController(ParserFactory parserFactory, ILogger<TestController> logger)
    {
        _parserFactory = parserFactory;
        _logger = logger;
    }

    [HttpGet("parse")]
    public async Task<IActionResult> ParseProduct([FromQuery] string url)
    {
        try
        {
            _logger.LogInformation("Parsing URL: {Url}", url);
            
            var parser = _parserFactory.GetParser(url);
            if (parser == null)
            {
                return BadRequest(new { Error = "No parser available for this URL" });
            }

            _logger.LogInformation("Using parser: {ParserType}", parser.GetType().Name);
            
            var result = await parser.ParseAsync(url);
            
            return Ok(new
            {
                result.Success,
                result.Name,
                result.Price,
                result.Currency,
                result.Source,
                ImageUrlPreview = result.ImageUrl?[..Math.Min(100, result.ImageUrl?.Length ?? 0)],
                result.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse URL: {Url}", url);
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
