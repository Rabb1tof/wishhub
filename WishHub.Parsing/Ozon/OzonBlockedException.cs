namespace WishHub.Parsing.Ozon;

public sealed class OzonBlockedException : Exception
{
    public OzonBlockedException(string message, string? currentUrl = null, int? statusCode = null)
        : base(message)
    {
        CurrentUrl = currentUrl;
        StatusCode = statusCode;
    }

    public string? CurrentUrl { get; }

    public int? StatusCode { get; }
}
