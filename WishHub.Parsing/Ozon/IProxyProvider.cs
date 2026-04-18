namespace WishHub.Parsing.Ozon;

public interface IProxyProvider
{
    string? GetNextProxy(IReadOnlyCollection<string> usedProxyUrls);
}
