namespace WishHub.Core.Entities;

public class Product
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public ProductSource Source { get; set; }
    public DateTime LastParsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
}

public enum ProductSource { Unknown, Wildberries, Ozon, YandexMarket }
