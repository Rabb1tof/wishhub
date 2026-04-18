namespace WishHub.Parsing.Ozon;

public sealed class NoOpProxyProvider : IProxyProvider
{
    public string? GetNextProxy(IReadOnlyCollection<string> usedProxyUrls) => null;
}
