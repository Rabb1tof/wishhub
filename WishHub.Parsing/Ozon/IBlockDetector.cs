using Microsoft.Playwright;

namespace WishHub.Parsing.Ozon;

public interface IBlockDetector
{
    Task EnsureNotBlockedAsync(IPage page, IResponse? response, Uri expectedUri, CancellationToken ct = default);
}
