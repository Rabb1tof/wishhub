using Microsoft.Extensions.Logging;
using WishHub.Core.Interfaces;

namespace WishHub.Infrastructure.BackgroundJobs;

public class PriceUpdateJob
{
    private readonly IProductService _productService;
    private readonly ILogger<PriceUpdateJob> _logger;

    public PriceUpdateJob(IProductService productService, ILogger<PriceUpdateJob> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("Starting price update job at {Time}", DateTime.UtcNow);
        
        try
        {
            await _productService.RefreshAllStaleProductsAsync();
            _logger.LogInformation("Price update job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Price update job failed");
            throw;
        }
    }
}
