namespace WishHub.Parsing.Ozon;

public sealed class OzonProduct
{
    public required string Url { get; init; }

    public required string Title { get; init; }

    public decimal Price { get; init; }

    public decimal? Rating { get; init; }

    public int? ReviewCount { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Images { get; init; } = [];
}
