using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class WishlistService : IWishlistService
{
    private readonly AppDbContext _dbContext;
    private readonly IProductService _productService;
    private readonly ILogger<WishlistService> _logger;

    public WishlistService(AppDbContext dbContext, IProductService productService, ILogger<WishlistService> logger)
    {
        _dbContext = dbContext;
        _productService = productService;
        _logger = logger;
    }

    public async Task<WishlistItemDto> AddItemAsync(Guid ownerId, AddWishlistItemRequest request, CancellationToken ct = default)
    {
        // Get or create product from URL
        var product = await _productService.GetOrCreateFromUrlAsync(request.Url, ct);

        // Check if already exists in wishlist
        var existing = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.OwnerId == ownerId && w.ProductId == product.Id, ct);

        if (existing != null)
        {
            throw new InvalidOperationException("Product already exists in wishlist");
        }

        // Get max sort order
        var maxOrder = await _dbContext.WishlistItems
            .Where(w => w.OwnerId == ownerId)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(ct) ?? 0;

        var item = new WishlistItem
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ProductId = product.Id,
            CustomName = request.CustomName,
            IsReserved = false,
            SortOrder = maxOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.WishlistItems.Add(item);
        await _dbContext.SaveChangesAsync(ct);

        return await MapToDtoAsync(item, ownerId, ct);
    }

    public async Task<WishlistItemDto?> UpdateItemAsync(Guid ownerId, Guid itemId, UpdateWishlistItemRequest request, CancellationToken ct = default)
    {
        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.OwnerId == ownerId, ct);

        if (item == null)
            return null;

        if (request.CustomName != null)
            item.CustomName = request.CustomName;

        if (request.SortOrder.HasValue)
            item.SortOrder = request.SortOrder.Value;

        await _dbContext.SaveChangesAsync(ct);

        return await MapToDtoAsync(item, ownerId, ct);
    }

    public async Task DeleteItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.OwnerId == ownerId, ct);

        if (item != null)
        {
            _dbContext.WishlistItems.Remove(item);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<WishlistItemDto>> GetItemsAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default)
    {
        // Проверяем доступ с учётом приватности
        if (!await CanViewWishlistAsync(ownerId, viewerId, ct))
            return Array.Empty<WishlistItemDto>();

        var query = _dbContext.WishlistItems
            .AsNoTracking()
            .Where(w => w.OwnerId == ownerId)
            .Include(w => w.Product)
            .Include(w => w.ReservedBy)
            .OrderBy(w => w.SortOrder)
            .AsQueryable();

        var items = await query.ToListAsync(ct);

        return items.Select(i => MapToDto(i, viewerId)).ToList();
    }

    public async Task<bool> CanViewWishlistAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default)
    {
        // Владелец всегда видит свой вишлист
        if (viewerId.HasValue && viewerId.Value == ownerId)
            return true;

        // Проверяем настройки приватности владельца
        var owner = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerId)
            .Select(u => new { u.WishlistPrivacy })
            .FirstOrDefaultAsync(ct);

        if (owner == null)
            return false;

        if (owner.WishlistPrivacy == WishlistPrivacy.Private)
            return false;

        if (owner.WishlistPrivacy == WishlistPrivacy.FriendsOnly)
        {
            if (!viewerId.HasValue)
                return false;

            return await _dbContext.Friendships
                .AsNoTracking()
                .AnyAsync(f =>
                    f.Status == FriendshipStatus.Accepted &&
                    ((f.RequesterId == viewerId.Value && f.AddresseeId == ownerId) ||
                     (f.AddresseeId == viewerId.Value && f.RequesterId == ownerId)), ct);
        }

        // Public
        return true;
    }

    public async Task<WishlistItemDto?> ReserveItemAsync(Guid itemId, Guid reserverId, CancellationToken ct = default)
    {
        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId, ct);

        if (item == null || item.IsReserved || item.OwnerId == reserverId)
            return null;

        item.IsReserved = true;
        item.ReservedById = reserverId;

        // Create notification for owner
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = item.OwnerId,
            Type = NotificationType.ItemReserved,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ItemId = item.Id,
                ProductId = item.ProductId,
                ReservedById = reserverId
            }),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);

        return await MapToDtoAsync(item, reserverId, ct);
    }

    public async Task<WishlistItemDto?> RefreshItemAsync(Guid itemId, Guid ownerId, CancellationToken ct = default)
    {
        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.OwnerId == ownerId, ct);

        if (item == null)
            return null;

        // Принудительно перепарсим товар
        await _productService.RefreshPriceAsync(item.ProductId, ct);

        return await MapToDtoAsync(item, ownerId, ct);
    }

    public async Task<WishlistItemDto?> CancelReservationAsync(Guid itemId, Guid reserverId, CancellationToken ct = default)
    {
        var item = await _dbContext.WishlistItems
            .FirstOrDefaultAsync(w => w.Id == itemId && w.ReservedById == reserverId, ct);

        if (item == null)
            return null;

        item.IsReserved = false;
        item.ReservedById = null;

        await _dbContext.SaveChangesAsync(ct);

        return await MapToDtoAsync(item, reserverId, ct);
    }

    private async Task<WishlistItemDto> MapToDtoAsync(WishlistItem item, Guid? viewerId, CancellationToken ct = default)
    {
        await _dbContext.Entry(item).Reference(w => w.Product).LoadAsync(ct);
        await _dbContext.Entry(item).Reference(w => w.ReservedBy).LoadAsync(ct);

        return MapToDto(item, viewerId);
    }

    private WishlistItemDto MapToDto(WishlistItem item, Guid? viewerId)
    {
        var imageUrl = item.Product.ImageUrl;
        var imageProxyUrl = !string.IsNullOrEmpty(imageUrl)
            ? $"/api/images/proxy?url={Uri.EscapeDataString(imageUrl)}"
            : null;

        // Показываем processing если продукт в процессе или ожидании парсинга
        var isProcessing = item.Product.ParsingStatus == ParsingStatus.Pending ||
                           item.Product.ParsingStatus == ParsingStatus.Processing;

        return new WishlistItemDto
        {
            Id = item.Id,
            CustomName = item.CustomName,
            IsReserved = item.IsReserved,
            IsReservedByMe = item.ReservedById == viewerId,
            SortOrder = item.SortOrder,
            CreatedAt = item.CreatedAt,
            IsProcessing = isProcessing,
            ProcessingError = item.Product.ParsingStatus == ParsingStatus.Failed
                ? item.Product.ParsingError
                : null,
            Product = new ProductDto
            {
                Id = item.Product.Id,
                Name = isProcessing ? (item.CustomName ?? "Загрузка...") : item.Product.Name,
                ImageUrl = item.Product.ImageUrl,
                ImageProxyUrl = isProcessing ? null : imageProxyUrl,
                Price = item.Product.Price,
                Currency = item.Product.Currency,
                Source = item.Product.Source.ToString(),
                Url = item.Product.Url
            },
            ReservedBy = item.ReservedBy != null && item.ReservedById == viewerId
                ? new UserDto
                {
                    Id = item.ReservedBy.Id,
                    Username = item.ReservedBy.UserName!,
                    DisplayName = item.ReservedBy.DisplayName,
                    AvatarUrl = item.ReservedBy.AvatarUrl
                }
                : null
        };
    }
}
