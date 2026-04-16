using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WishHub.Core.Entities;

namespace WishHub.Infrastructure.Data.Configurations;

public class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique();
        
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.AccessToken).HasMaxLength(500);
        
        builder.HasOne(e => e.User)
            .WithMany(u => u.ExternalLogins)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
