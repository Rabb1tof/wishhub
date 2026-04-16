using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WishHub.Core.Entities;

namespace WishHub.Infrastructure.Data.Configurations;

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasIndex(w => new { w.OwnerId, w.ProductId });
        
        builder.Property(w => w.CustomName).HasMaxLength(500);
        
        builder.HasOne(w => w.Owner)
            .WithMany(u => u.WishlistItems)
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(w => w.Product)
            .WithMany(p => p.WishlistItems)
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(w => w.ReservedBy)
            .WithMany()
            .HasForeignKey(w => w.ReservedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
