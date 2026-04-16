using WishHub.Core.DTOs.Wishlist;

namespace WishHub.Core.Interfaces;

public interface IWishlistService
{
    Task<WishlistItemDto> AddItemAsync(Guid ownerId, AddWishlistItemRequest request, CancellationToken ct = default);
    Task<WishlistItemDto?> UpdateItemAsync(Guid ownerId, Guid itemId, UpdateWishlistItemRequest request, CancellationToken ct = default);
    Task DeleteItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<WishlistItemDto>> GetItemsAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default);
    Task<WishlistItemDto?> ReserveItemAsync(Guid itemId, Guid reserverId, CancellationToken ct = default);
    Task<WishlistItemDto?> CancelReservationAsync(Guid itemId, Guid reserverId, CancellationToken ct = default);
    Task<bool> CanViewWishlistAsync(Guid ownerId, Guid? viewerId, CancellationToken ct = default);
    Task<WishlistItemDto?> RefreshItemAsync(Guid itemId, Guid ownerId, CancellationToken ct = default);
}
