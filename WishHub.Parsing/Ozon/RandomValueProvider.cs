namespace WishHub.Parsing.Ozon;

public sealed class RandomValueProvider : IRandomValueProvider
{
    public int Next(int minValue, int maxValueExclusive) => Random.Shared.Next(minValue, maxValueExclusive);
}
