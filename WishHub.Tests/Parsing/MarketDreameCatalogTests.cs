using WishHub.Parsing;

namespace WishHub.Tests.Parsing;

public sealed class MarketDreameCatalogTests
{
    private const string BlackImage = "https://static.price.ru/-/7107-xiaomi-dreame-f21/3b3743c44ac77cc1394666a1c98dcb05/source/dreame-f21-black.jpg";
    private const string WhiteImage = "https://static.price.ru/-/7107-xiaomi-dreame-f21/053be7fefedd353353364b2efbc039aa/source/dreame-f21-white.jpg";
    private const string BlackName = "Пылесос Dreame F21 Black (Черный) (EAC)";
    private const string WhiteName = "Робот-пылесос Dreame Bot F21 белый (RLF42GA)";

    [Theory]
    [InlineData("black", BlackName, BlackImage)]
    [InlineData("white", WhiteName, WhiteImage)]
    public void Find_MixedOffers_ReturnsMatchingBaseModelAndItsOwnColorPhoto(string color, string expectedName, string expectedImage)
    {
        var html = Offer("Робот-пылесос Dreame F21 Pro чёрный (RLF23SA)", WhiteImage) +
            Offer(WhiteName, WhiteImage) +
            Offer("Пылесос Dreame F21 Plus Black", WhiteImage) +
            Offer(BlackName, BlackImage);

        Assert.Equal((expectedName, expectedImage), MarketDreameCatalog.Find(html, color));
    }

    [Theory]
    [InlineData("Пылесос Dreame F21 Ultra Black")]
    [InlineData("Пылесос Dreame F21 Black (Белый)")]
    [InlineData("Пылесос Dreame F21 White")]
    [InlineData("Щётка для Dreame F21 Black")]
    [InlineData("Пылесос Dreame F21 Black (RLF23SA)")]
    public void Find_WrongOrAmbiguousVariant_DoesNotSuggestIt(string name) =>
        Assert.Equal((null, null), MarketDreameCatalog.Find(Offer(name, BlackImage), "black"));

    [Theory]
    [InlineData("https://static.price.ru.example/-/7107-xiaomi-dreame-f21/3b3743c44ac77cc1394666a1c98dcb05/source/f21.jpg")]
    [InlineData("https://static.price.ru/logo.jpg")]
    [InlineData("https://static.price.ru:8443/-/7107-xiaomi-dreame-f21/3b3743c44ac77cc1394666a1c98dcb05/source/f21.jpg")]
    [InlineData("http://static.price.ru/-/7107-xiaomi-dreame-f21/3b3743c44ac77cc1394666a1c98dcb05/source/f21.jpg")]
    [InlineData("https://static.price.ru/-/7107-xiaomi-dreame-f21/3b3743c44ac77cc1394666a1c98dcb05/source/f21.svg")]
    public void Find_UnexpectedImageOriginOrPath_DoesNotSuggestIt(string image) =>
        Assert.Equal((null, null), MarketDreameCatalog.Find(Offer(BlackName, image), "black"));

    [Fact]
    public void Find_MatchingOfferWithoutImage_DoesNotBorrowAdjacentOfferImage()
    {
        var html = $"""<div class="p-c-price__image" data-v-price><meta itemprop="image"><img alt="{BlackName}"></div>""" +
            Offer(WhiteName, WhiteImage);

        Assert.Equal((null, null), MarketDreameCatalog.Find(html, "black"));
    }

    private static string Offer(string name, string image) =>
        $"""<div class="p-c-price__image" data-v-price><meta itemprop="image" content="{image}" data-v-price><!----><img alt="{name}" class="p-c-price__img"></div>""";
}
