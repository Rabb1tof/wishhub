namespace WishHub.Core.DTOs.Wishlist;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ImageProxyUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
