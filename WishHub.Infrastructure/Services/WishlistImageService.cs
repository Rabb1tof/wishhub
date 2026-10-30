using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public sealed class WishlistImageService(
    AppDbContext dbContext, IWishlistService wishlistService, IWishlistImageStorage storage) : IWishlistImageService
{
    public async Task<string> UploadAsync(Guid ownerId, Stream content, string? contentType, CancellationToken ct = default)
    {
        // The same owner row lock protects card saves from concurrent orphan cleanup.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE", ct);
        if (!await dbContext.Users.AnyAsync(user => user.Id == ownerId, ct))
            throw new ValidationException("Wishlist owner does not exist.");

        var references = await dbContext.WishlistItems.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.ImageUrl != null)
            .Select(item => item.ImageUrl!).ToListAsync(ct);
        // No database rows are written; disposing the transaction releases the cleanup/save lock.
        return await storage.UploadAsync(ownerId, content, contentType, references.ToHashSet(StringComparer.Ordinal), ct);
    }

    public async Task<Stream?> OpenReadAsync(ManagedWishlistImage image, Guid? viewerId, CancellationToken ct = default)
    {
        if (!ManagedWishlistImage.TryParse(image.Url, out _))
            return null;
        if (viewerId != image.OwnerId)
        {
            if (!await dbContext.WishlistItems.AsNoTracking()
                    .AnyAsync(item => item.OwnerId == image.OwnerId && item.ImageUrl == image.Url, ct) ||
                !await wishlistService.CanViewWishlistAsync(image.OwnerId, viewerId, ct))
                return null;
        }
        ct.ThrowIfCancellationRequested();
        return storage.OpenRead(image);
    }
}
