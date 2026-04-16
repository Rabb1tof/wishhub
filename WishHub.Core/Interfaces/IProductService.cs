using WishHub.Core.Entities;

namespace WishHub.Core.Interfaces;

public interface IProductService
{
    Task<Product> GetOrCreateFromUrlAsync(string url, CancellationToken ct = default);
    Task RefreshPriceAsync(Guid productId, CancellationToken ct = default);
    Task RefreshAllStaleProductsAsync(CancellationToken ct = default);
}
