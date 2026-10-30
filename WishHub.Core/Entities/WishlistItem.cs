namespace WishHub.Core.Entities;

public class WishlistItem
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ProductId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "RUB";
    public ProductSource Source { get; set; }
    public bool IsReserved { get; set; }
    public Guid? ReservedById { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public Product? Product { get; set; }
    public User? ReservedBy { get; set; }
}
