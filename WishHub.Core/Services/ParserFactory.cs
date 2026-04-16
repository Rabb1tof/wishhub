using WishHub.Core.Interfaces;

namespace WishHub.Core.Services;

public class ParserFactory
{
    private readonly IEnumerable<IProductParser> _parsers;

    public ParserFactory(IEnumerable<IProductParser> parsers)
        => _parsers = parsers;

    public IProductParser? GetParser(string url)
        => _parsers.FirstOrDefault(p => p.CanParse(url));
}
