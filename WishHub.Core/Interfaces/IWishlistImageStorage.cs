using WishHub.Core.Validation;

namespace WishHub.Core.Interfaces;

public interface IWishlistImageStorage
{
    // The caller serializes owner uploads/cleanup with card saves.
    Task<string> UploadAsync(Guid ownerId, Stream content, string? contentType,
        IReadOnlySet<string> referencedImageUrls, CancellationToken ct = default);
    bool Exists(ManagedWishlistImage image);
    Stream? OpenRead(ManagedWishlistImage image);
}
