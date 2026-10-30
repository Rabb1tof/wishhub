using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WishHub.Core.Entities;

namespace WishHub.Infrastructure.Data.Configurations;

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasIndex(w => new { w.OwnerId, w.ProductId });

        // Retained only for a lossless rollback of legacy cards until data cleanup.
        builder.Property<string?>("CustomName").HasMaxLength(500);
        builder.Property(w => w.Url).HasMaxLength(2000).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(500).IsRequired();
        builder.Property(w => w.ImageUrl).HasMaxLength(2000);
        builder.Property(w => w.Currency).HasMaxLength(10).IsRequired();

        builder.HasOne(w => w.Owner)
            .WithMany(u => u.WishlistItems)
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.Product)
            .WithMany(p => p.WishlistItems)
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(w => w.ReservedBy)
            .WithMany()
            .HasForeignKey(w => w.ReservedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
