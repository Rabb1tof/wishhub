using WishHub.Core.DTOs;

namespace WishHub.Core.Interfaces;

public interface IProductMetadataService
{
    Task<ProductMetadataDto> ResolveAsync(string url, CancellationToken ct = default);
}
