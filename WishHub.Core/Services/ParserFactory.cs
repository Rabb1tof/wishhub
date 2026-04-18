using WishHub.Core.Entities;
using WishHub.Core.Interfaces;

namespace WishHub.Core.Services;

public class ParserFactory
{
    private readonly IEnumerable<IProductParser> _parsers;

    public ParserFactory(IEnumerable<IProductParser> parsers)
        => _parsers = parsers;

    public IProductParser? GetParser(string url)
        => _parsers.FirstOrDefault(p => p.CanParse(url));

    /// <summary>
    /// Определяет источник товара по URL
    /// </summary>
    public ProductSource ParseSource(string url)
    {
        if (url.Contains("wildberries", StringComparison.OrdinalIgnoreCase))
            return ProductSource.Wildberries;
        if (url.Contains("ozon", StringComparison.OrdinalIgnoreCase))
            return ProductSource.Ozon;
        if (url.Contains("market.yandex", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("beru", StringComparison.OrdinalIgnoreCase))
            return ProductSource.YandexMarket;

        return ProductSource.Unknown;
    }
}
