using WishHub.Core.Models;

namespace WishHub.Core.Interfaces;

public interface IProductParser
{
    bool CanParse(string url);
    Task<ParseResult> ParseAsync(string url, CancellationToken ct = default);
}
