namespace WishHub.Parsing.Ozon;

public interface IOzonProductScraper
{
    Task<OzonProduct> ScrapeProductAsync(string productUrl, CancellationToken ct = default);
}
