namespace WishHub.Parsing.Ozon;

public interface IRandomValueProvider
{
    int Next(int minValue, int maxValueExclusive);
}
