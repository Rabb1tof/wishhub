using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WishHub.Core.Entities;

namespace WishHub.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(p => p.Url).IsUnique();
        builder.HasIndex(p => p.Source);
        
        builder.Property(p => p.Url).HasMaxLength(2000);
        builder.Property(p => p.Name).HasMaxLength(500);
        builder.Property(p => p.ImageUrl).HasMaxLength(2000);
        builder.Property(p => p.Currency).HasMaxLength(3);
    }
}
