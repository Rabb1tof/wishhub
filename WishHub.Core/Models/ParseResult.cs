using WishHub.Core.Entities;

namespace WishHub.Core.Models;

public class ParseResult
{
    public bool Success { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? ErrorMessage { get; set; }
    public ProductSource Source { get; set; }
}
