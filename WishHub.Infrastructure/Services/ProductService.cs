using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Core.Services;
using WishHub.Infrastructure.Data;

namespace WishHub.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _dbContext;
    private readonly ParserFactory _parserFactory;
    private readonly ILogger<ProductService> _logger;

    public ProductService(AppDbContext dbContext, ParserFactory parserFactory, ILogger<ProductService> logger)
    {
        _dbContext = dbContext;
        _parserFactory = parserFactory;
        _logger = logger;
    }

    public async Task<Product> GetOrCreateFromUrlAsync(string url, CancellationToken ct = default)
    {
        // Normalize URL
        url = url.Trim();

        // Check if product already exists
        var existingProduct = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Url == url, ct);

        // Если товар уже есть и обновлялся недавно (< 6 часов) — возвращаем как есть.
        // Это кэш для добавления в вишлист нескольких друзей, чтобы не перепарсивать каждый раз.
        if (existingProduct != null && existingProduct.LastParsedAt > DateTime.UtcNow.AddHours(-6))
        {
            return existingProduct;
        }

        return await ParseAndPersistAsync(url, existingProduct, ct);
    }

    public async Task RefreshPriceAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FindAsync(new object[] { productId }, ct);
        if (product == null)
        {
            throw new InvalidOperationException($"Product with ID {productId} not found");
        }

        // Принудительное обновление, минуя кэш
        await ParseAndPersistAsync(product.Url, product, ct);
    }

    private async Task<Product> ParseAndPersistAsync(string url, Product? existingProduct, CancellationToken ct)
    {
        var parser = _parserFactory.GetParser(url);
        if (parser == null)
        {
            throw new InvalidOperationException($"No parser available for URL: {url}");
        }

        var parseResult = await parser.ParseAsync(url, ct);
        if (!parseResult.Success)
        {
            throw new InvalidOperationException($"Failed to parse product: {parseResult.ErrorMessage}");
        }

        if (existingProduct != null)
        {
            var oldPrice = existingProduct.Price;
            var oldImage = existingProduct.ImageUrl;

            existingProduct.Name = parseResult.Name ?? existingProduct.Name;
            existingProduct.ImageUrl = parseResult.ImageUrl ?? existingProduct.ImageUrl;
            existingProduct.Price = parseResult.Price ?? existingProduct.Price;
            existingProduct.Currency = parseResult.Currency ?? existingProduct.Currency;
            existingProduct.LastParsedAt = DateTime.UtcNow;

            if (oldPrice != existingProduct.Price)
            {
                _logger.LogInformation(
                    "Product {ProductId} price changed: {OldPrice} -> {NewPrice}",
                    existingProduct.Id, oldPrice, existingProduct.Price);
            }
            if (oldImage != existingProduct.ImageUrl)
            {
                _logger.LogInformation("Product {ProductId} image URL changed", existingProduct.Id);
            }

            // Уведомления владельцам при изменении цены
            if (oldPrice.HasValue && parseResult.Price.HasValue && oldPrice != parseResult.Price)
            {
                await CreatePriceChangeNotificationsAsync(existingProduct.Id, oldPrice.Value, parseResult.Price.Value, ct);
            }

            await _dbContext.SaveChangesAsync(ct);
            return existingProduct;
        }

        var newProduct = new Product
        {
            Id = Guid.NewGuid(),
            Url = url,
            Name = parseResult.Name ?? "Unknown Product",
            ImageUrl = parseResult.ImageUrl,
            Price = parseResult.Price,
            Currency = parseResult.Currency ?? "RUB",
            Source = parseResult.Source,
            LastParsedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Products.Add(newProduct);
        await _dbContext.SaveChangesAsync(ct);

        return newProduct;
    }

    public async Task RefreshAllStaleProductsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Берём все продукты, которые находятся хоть в одном вишлисте,
        // вместе с минимальным интервалом обновления среди владельцев.
        // Продукт считается устаревшим, если LastParsedAt < now - minInterval.
        var candidates = await _dbContext.WishlistItems
            .AsNoTracking()
            .GroupBy(w => w.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                MinIntervalHours = g.Min(w => w.Owner.PriceRefreshIntervalHours)
            })
            .Join(_dbContext.Products,
                x => x.ProductId,
                p => p.Id,
                (x, p) => new { Product = p, x.MinIntervalHours })
            .ToListAsync(ct);

        var staleIds = candidates
            .Where(c => c.Product.LastParsedAt < now.AddHours(-c.MinIntervalHours))
            .Select(c => c.Product.Id)
            .ToList();

        _logger.LogInformation("Found {Count} stale products to refresh", staleIds.Count);

        foreach (var productId in staleIds)
        {
            try
            {
                await RefreshPriceAsync(productId, ct);
                _logger.LogInformation("Refreshed product: {ProductId}", productId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh product: {ProductId}", productId);
            }

            // Небольшая задержка, чтобы не перегружать парсеры
            await Task.Delay(500, ct);
        }
    }

    private async Task CreatePriceChangeNotificationsAsync(Guid productId, decimal oldPrice, decimal newPrice, CancellationToken ct)
    {
        // Find all users who have this product in their wishlist
        var ownerIds = await _dbContext.WishlistItems
            .Where(w => w.ProductId == productId)
            .Select(w => w.OwnerId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var ownerId in ownerIds)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = ownerId,
                Type = NotificationType.PriceChanged,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ProductId = productId,
                    OldPrice = oldPrice,
                    NewPrice = newPrice
                }),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Notifications.Add(notification);
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
