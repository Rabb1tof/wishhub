using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Validation;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class WishlistService(AppDbContext dbContext, IWishlistImageStorage imageStorage) : IWishlistService
{
    public async Task<WishlistItemDto> AddItemAsync(Guid ownerId, AddWishlistItemRequest request, CancellationToken ct = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(ownerId, ct);
        var item = new WishlistItem
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow
        };
        ValidateImageReference(ownerId, request.ImageUrl);
        ApplyValues(item, request);
        await EnsureUniqueUrlAsync(ownerId, item.Url, null, ct);

        var maxOrder = await dbContext.WishlistItems
            .Where(w => w.OwnerId == ownerId)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(ct) ?? 0;
        item.SortOrder = maxOrder + 1;

        dbContext.WishlistItems.Add(item);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapToDto(item, ownerId);
    }

    public async Task<WishlistItemDto?> UpdateItemAsync(Guid ownerId, Guid itemId, UpdateWishlistItemRequest request, CancellationToken ct = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(ownerId, ct);
        var item = await dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.OwnerId == ownerId, ct);
        if (item == null)
            return null;

        // Validate before touching tracked state, including duplicate conflicts.
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var url = WishlistValues.NormalizeUrl(request.Url);
        await EnsureUniqueUrlAsync(ownerId, url, itemId, ct);
        ValidateImageReference(ownerId, request.ImageUrl);
        ApplyValues(item, request);
        if (request.SortOrder.HasValue)
            item.SortOrder = request.SortOrder.Value;

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapToDto(item, ownerId);
    }

    public async Task DeleteItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.OwnerId == ownerId, ct);
        if (item == null)
            return;
        dbContext.WishlistItems.Remove(item);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WishlistItemDto>> GetItemsAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default)
    {
        if (!await CanViewWishlistAsync(ownerId, viewerId, ct))
            return Array.Empty<WishlistItemDto>();
        var items = await dbContext.WishlistItems.AsNoTracking()
            .Where(w => w.OwnerId == ownerId)
            .Include(w => w.ReservedBy)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.CreatedAt)
            .ToListAsync(ct);
        return items.Select(i => MapToDto(i, viewerId)).ToList();
    }

    public async Task<bool> CanViewWishlistAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default)
    {
        if (viewerId == ownerId)
            return true;
        var owner = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == ownerId).Select(u => new { u.WishlistPrivacy })
            .FirstOrDefaultAsync(ct);
        if (owner == null || owner.WishlistPrivacy == WishlistPrivacy.Private)
            return false;
        if (owner.WishlistPrivacy != WishlistPrivacy.FriendsOnly)
            return true;
        if (!viewerId.HasValue)
            return false;
        return await dbContext.Friendships.AsNoTracking().AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == viewerId.Value && f.AddresseeId == ownerId) ||
             (f.AddresseeId == viewerId.Value && f.RequesterId == ownerId)), ct);
    }

    public async Task<WishlistItemDto?> ReserveItemAsync(Guid itemId, Guid reserverId, CancellationToken ct = default)
    {
        var item = await dbContext.WishlistItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == itemId, ct);
        if (item == null || item.IsReserved || item.OwnerId == reserverId ||
            !await CanViewWishlistAsync(item.OwnerId, reserverId, ct))
            return null;

        var updated = await dbContext.WishlistItems
            .Where(w => w.Id == itemId && !w.IsReserved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.IsReserved, true)
                .SetProperty(w => w.ReservedById, reserverId), ct);
        if (updated == 0)
            return null;

        // A notification to the owner would reveal the surprise reservation.
        item.IsReserved = true;
        item.ReservedById = reserverId;
        item.ReservedBy = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == reserverId, ct);
        return MapToDto(item, reserverId);
    }

    public async Task<WishlistItemDto?> CancelReservationAsync(Guid itemId, Guid reserverId, CancellationToken ct = default)
    {
        var item = await dbContext.WishlistItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == itemId && w.ReservedById == reserverId && w.IsReserved, ct);
        if (item == null)
            return null;
        var updated = await dbContext.WishlistItems
            .Where(w => w.Id == itemId && w.ReservedById == reserverId && w.IsReserved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.IsReserved, false)
                .SetProperty(w => w.ReservedById, (Guid?)null), ct);
        if (updated == 0)
            return null;
        item.IsReserved = false;
        item.ReservedById = null;
        return MapToDto(item, reserverId);
    }

    private async Task LockOwnerAsync(Guid ownerId, CancellationToken ct)
    {
        // Legacy duplicate rows must survive migration, so serialize URL checks per owner.
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE", ct);
    }
    private async Task EnsureUniqueUrlAsync(Guid ownerId, string url, Guid? excludedId, CancellationToken ct)
    {
        if (await dbContext.WishlistItems.AnyAsync(w =>
                w.OwnerId == ownerId && w.Url == url && (!excludedId.HasValue || w.Id != excludedId.Value), ct))
            throw new InvalidOperationException("URL already exists in wishlist.");
    }

    private void ValidateImageReference(Guid ownerId, string? imageUrl)
    {
        if (imageUrl is null)
            return;
        var normalized = WishlistValues.NormalizeImageUrl(imageUrl);
        if (ManagedWishlistImage.TryParse(normalized, out var image) &&
            (image.OwnerId != ownerId || !imageStorage.Exists(image)))
            throw new ValidationException("Managed image must belong to you and exist in storage.");
    }
    private static void ApplyValues(WishlistItem item, AddWishlistItemRequest request)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        item.Url = WishlistValues.NormalizeUrl(request.Url);
        item.Name = WishlistValues.NormalizeName(request.Name);
        item.ImageUrl = request.ImageUrl is null ? null : WishlistValues.NormalizeImageUrl(request.ImageUrl);
        item.Price = request.Price;
        item.Currency = WishlistValues.NormalizeCurrency(request.Currency);
        item.Source = WishlistValues.GetSource(item.Url);
    }

    private static WishlistItemDto MapToDto(WishlistItem item, Guid? viewerId)
    {
        var isOwner = item.OwnerId == viewerId;
        var reservedByMe = !isOwner && viewerId.HasValue && item.IsReserved && item.ReservedById == viewerId;
        return new WishlistItemDto
        {
            Id = item.Id,
            Url = item.Url,
            Name = item.Name,
            ImageUrl = item.ImageUrl,
            Price = item.Price,
            Currency = item.Currency,
            Source = item.Source.ToString(),
            IsReserved = !isOwner && item.IsReserved,
            IsReservedByMe = reservedByMe,
            SortOrder = item.SortOrder,
            CreatedAt = item.CreatedAt,
            ReservedBy = reservedByMe && item.ReservedBy != null ? new UserDto
            {
                Id = item.ReservedBy.Id,
                Username = item.ReservedBy.UserName!,
                DisplayName = item.ReservedBy.DisplayName,
                AvatarUrl = item.ReservedBy.AvatarUrl
            } : null
        };
    }
}
