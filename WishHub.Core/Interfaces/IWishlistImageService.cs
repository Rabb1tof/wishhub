using WishHub.Core.Validation;

namespace WishHub.Core.Interfaces;

public interface IWishlistImageService
{
    Task<string> UploadAsync(Guid ownerId, Stream content, string? contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(ManagedWishlistImage image, Guid? viewerId, CancellationToken ct = default);
}
