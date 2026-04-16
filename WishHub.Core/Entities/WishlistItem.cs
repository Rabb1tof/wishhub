namespace WishHub.Core.Entities;

public class WishlistItem
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ProductId { get; set; }
    public string? CustomName { get; set; }
    public bool IsReserved { get; set; }
    public Guid? ReservedById { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public User? ReservedBy { get; set; }
}
