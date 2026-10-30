using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Entities;
using WishHub.Core.Validation;

namespace WishHub.Tests.Infrastructure;

public class WishlistServiceTests
{
    [Fact]
    public async Task AddItem_OnlyUrlAndName_PersistsSnapshotWithoutProduct()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var result = await store.Service.AddItemAsync(owner.Id, new()
        {
            Url = "  HTTPS://SHOP.EXAMPLE/Thing?sku=AB%2fCd&variant=2#Blue  ", Name = "  My gift  "
        });
        store.Db.ChangeTracker.Clear();
        var saved = await store.Db.WishlistItems.SingleAsync();
        Assert.Equal("https://shop.example/Thing?sku=AB%2fCd&variant=2#Blue", saved.Url);
        Assert.Equal("My gift", saved.Name);
        Assert.Null(saved.ProductId);
        Assert.Null(saved.ImageUrl);
        Assert.Null(saved.Price);
        Assert.Equal("RUB", saved.Currency);
        Assert.Equal(ProductSource.Unknown, saved.Source);
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal(1, result.SortOrder);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.InRange(result.CreatedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Empty(await store.Db.Products.ToListAsync());
    }

    [Fact]
    public async Task UpdateItem_ReplacesAndClearsOptionalValues_PreservesOrderAndIdentity()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var original = await store.Service.AddItemAsync(owner.Id, new()
        {
            Url = "https://shop.example/p", Name = "Original", ImageUrl = "https://cdn.example/p.jpg",
            Price = 12.345m, Currency = "USD"
        });
        var replaced = await store.Service.UpdateItemAsync(owner.Id, original.Id, new()
        {
            Url = "HTTPS://WWW.OZON.RU/product/p?variant=5", Name = " Changed ",
            ImageUrl = "HTTPS://CDN.EXAMPLE/another.jpg", Price = 0m, Currency = "EUR", SortOrder = 17
        });
        Assert.Equal("Changed", replaced!.Name);
        Assert.Equal("https://cdn.example/another.jpg", replaced.ImageUrl);
        Assert.Equal(0m, replaced.Price);
        Assert.Equal("EUR", replaced.Currency);
        Assert.Equal("Ozon", replaced.Source);
        var cleared = await store.Service.UpdateItemAsync(owner.Id, original.Id, new()
        {
            Url = replaced.Url, Name = "Kept without optionals", ImageUrl = null, Price = null
        });
        Assert.Null(cleared!.ImageUrl);
        Assert.Null(cleared.Price);
        Assert.Equal(17, cleared.SortOrder);
        Assert.Equal(original.Id, cleared.Id);
        Assert.Equal(original.CreatedAt, cleared.CreatedAt);
        Assert.Equal("RUB", cleared.Currency);
        store.Db.ChangeTracker.Clear();
        var saved = await store.Db.WishlistItems.SingleAsync();
        Assert.Null(saved.ImageUrl);
        Assert.Null(saved.Price);
        Assert.Null(saved.ProductId);
    }

    [Fact]
    public async Task Save_DuplicatesAreOwnerScoped_ExcludesSelfAndPreservesVariants()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var first = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/p?sku=1", Name = "One" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Service.AddItemAsync(owner.Id,
            new() { Url = " HTTPS://SHOP.EXAMPLE/p?sku=1 ", Name = "Duplicate" }));
        var variant = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/p?sku=2", Name = "Two" });
        Assert.NotEqual(first.Id, variant.Id);
        var independent = await store.Service.AddItemAsync(other.Id, new()
        {
            Url = first.Url, Name = "Another owner's version", Price = 99m, ImageUrl = "https://cdn.example/b.jpg"
        });
        var same = await store.Service.UpdateItemAsync(owner.Id, first.Id, new() { Url = first.Url, Name = "Renamed" });
        Assert.Equal(first.Id, same!.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Service.UpdateItemAsync(owner.Id, first.Id,
            new() { Url = variant.Url, Name = "Duplicate edit" }));
        var preserved = await store.Db.WishlistItems.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal("Renamed", preserved.Name);
        Assert.Equal(first.Url, preserved.Url);
        await store.Service.DeleteItemAsync(owner.Id, first.Id);
        var others = await store.Service.GetItemsAsync(other.Id, other.Id);
        Assert.Single(others);
        Assert.Equal(independent.Id, others[0].Id);
        Assert.Equal("Another owner's version", others[0].Name);
        Assert.Equal(99m, others[0].Price);
        Assert.Equal("https://cdn.example/b.jpg", others[0].ImageUrl);
    }

    [Fact]
    public async Task UpdateAndDelete_NonOwner_CannotChangeCard()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var card = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/p", Name = "Keep" });
        Assert.Null(await store.Service.UpdateItemAsync(other.Id, card.Id, new() { Url = card.Url, Name = "Attacker" }));
        Assert.Null(await store.Service.UpdateItemAsync(owner.Id, Guid.NewGuid(), new() { Url = card.Url, Name = "Missing" }));
        await store.Service.DeleteItemAsync(other.Id, card.Id);
        Assert.Equal("Keep", (await store.Db.WishlistItems.SingleAsync()).Name);
        await store.Service.DeleteItemAsync(owner.Id, card.Id);
        Assert.Empty(await store.Service.GetItemsAsync(owner.Id, owner.Id));
    }

    [Fact]
    public async Task ReserveAndCancel_HidesFromOwnerAndOnlyReserverCanCancel()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var reserver = await store.AddUserAsync();
        var stranger = await store.AddUserAsync();
        var card = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/p", Name = "Surprise" });
        Assert.Null(await store.Service.ReserveItemAsync(card.Id, owner.Id));
        var reserved = await store.Service.ReserveItemAsync(card.Id, reserver.Id);
        Assert.True(reserved!.IsReserved);
        Assert.True(reserved.IsReservedByMe);
        Assert.Equal(reserver.Id, reserved.ReservedBy!.Id);
        Assert.Equal(reserver.UserName, reserved.ReservedBy.Username);
        Assert.Null(await store.Service.ReserveItemAsync(card.Id, stranger.Id));
        var ownerCard = Assert.Single(await store.Service.GetItemsAsync(owner.Id, owner.Id));
        Assert.False(ownerCard.IsReserved);
        Assert.False(ownerCard.IsReservedByMe);
        Assert.Null(ownerCard.ReservedBy);
        var strangerCard = Assert.Single(await store.Service.GetItemsAsync(owner.Id, stranger.Id));
        Assert.True(strangerCard.IsReserved);
        Assert.False(strangerCard.IsReservedByMe);
        Assert.Null(strangerCard.ReservedBy);
        Assert.Empty(await store.Db.Notifications.ToListAsync());
        var edited = await store.Service.UpdateItemAsync(owner.Id, card.Id, new() { Url = card.Url, Name = "Still a surprise" });
        Assert.False(edited!.IsReserved);
        Assert.Null(edited.ReservedBy);
        Assert.True((await store.Db.WishlistItems.AsNoTracking().SingleAsync()).IsReserved);
        Assert.Null(await store.Service.CancelReservationAsync(card.Id, owner.Id));
        Assert.Null(await store.Service.CancelReservationAsync(card.Id, stranger.Id));
        var cancelled = await store.Service.CancelReservationAsync(card.Id, reserver.Id);
        Assert.False(cancelled!.IsReserved);
        Assert.False(cancelled.IsReservedByMe);
        Assert.Null(cancelled.ReservedBy);
        var saved = await store.Db.WishlistItems.AsNoTracking().SingleAsync();
        Assert.False(saved.IsReserved);
        Assert.Null(saved.ReservedById);
        Assert.Null(await store.Service.CancelReservationAsync(card.Id, reserver.Id));
    }

    [Theory]
    [InlineData(WishlistPrivacy.Private, FriendshipStatus.Accepted, false)]
    [InlineData(WishlistPrivacy.FriendsOnly, FriendshipStatus.Pending, false)]
    [InlineData(WishlistPrivacy.FriendsOnly, FriendshipStatus.Blocked, false)]
    [InlineData(WishlistPrivacy.FriendsOnly, FriendshipStatus.Accepted, true)]
    [InlineData(WishlistPrivacy.Public, FriendshipStatus.Pending, true)]
    public async Task Privacy_ReadAndReserve_RespectVisibility(WishlistPrivacy privacy, FriendshipStatus friendship, bool allowed)
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync(privacy);
        var viewer = await store.AddUserAsync();
        store.Db.Friendships.Add(new()
        {
            Id = Guid.NewGuid(), RequesterId = viewer.Id, AddresseeId = owner.Id, Status = friendship, CreatedAt = DateTime.UtcNow
        });
        await store.Db.SaveChangesAsync();
        var card = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/p", Name = "Gift" });
        Assert.True(await store.Service.CanViewWishlistAsync(owner.Id, owner.Id));
        Assert.Equal(allowed, await store.Service.CanViewWishlistAsync(owner.Id, viewer.Id));
        Assert.Equal(allowed ? 1 : 0, (await store.Service.GetItemsAsync(owner.Id, viewer.Id)).Count);
        Assert.Equal(allowed, await store.Service.ReserveItemAsync(card.Id, viewer.Id) != null);
        Assert.Equal(privacy == WishlistPrivacy.Public, await store.Service.CanViewWishlistAsync(owner.Id, null));
        Assert.False(await store.Service.CanViewWishlistAsync(Guid.NewGuid(), viewer.Id));
    }

    [Fact]
    public async Task AddAndUpdate_InvalidValues_RejectBeforePersisting()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var card = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/valid", Name = "Keep" });
        foreach (var invalid in InvalidRequests())
        {
            await Assert.ThrowsAsync<ValidationException>(() => store.Service.AddItemAsync(owner.Id, invalid));
            await Assert.ThrowsAsync<ValidationException>(() => store.Service.UpdateItemAsync(owner.Id, card.Id, new()
            {
                Url = invalid.Url, Name = invalid.Name, ImageUrl = invalid.ImageUrl, Price = invalid.Price, Currency = invalid.Currency
            }));
        }
        Assert.Equal("Keep", (await store.Db.WishlistItems.AsNoTracking().SingleAsync()).Name);
    }


    [Theory]
    [InlineData("name")]
    [InlineData("currency")]
    public async Task AddAndUpdate_NulText_RejectsWithoutChangingCard(string field)
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var card = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/nul", Name = "Keep" });
        var request = new UpdateWishlistItemRequest
        {
            Url = card.Url,
            Name = field == "name" ? "a\0b" : "Changed",
            Currency = field == "currency" ? "R\0B" : "RUB"
        };
        await Assert.ThrowsAsync<ValidationException>(() => store.Service.AddItemAsync(owner.Id, request));
        await Assert.ThrowsAsync<ValidationException>(() => store.Service.UpdateItemAsync(owner.Id, card.Id, request));
        var saved = await store.Db.WishlistItems.AsNoTracking().SingleAsync();
        Assert.Equal("Keep", saved.Name);
        Assert.Equal("RUB", saved.Currency);
    }
    private static IEnumerable<AddWishlistItemRequest> InvalidRequests()
    {
        yield return new() { Url = "https://shop.example/p", Name = "   " };
        yield return new() { Url = "https://shop.example/p", Name = new string('n', 501) };
        yield return new() { Url = "https://shop.example/" + new string('x', 2000), Name = "Gift" };
        yield return new() { Url = "ftp://shop.example/p", Name = "Gift" };
        yield return new() { Url = "/relative", Name = "Gift" };
        yield return new() { Url = "https://user:pass@shop.example/p", Name = "Gift" };
        yield return new() { Url = "https://shop.example/p", Name = "Gift", ImageUrl = "javascript:alert(1)" };
        yield return new() { Url = "https://shop.example/p", Name = "Gift", Price = -0.01m };
        yield return new() { Url = "https://shop.example/p", Name = "Gift", Currency = new string('x', 11) };
    }

    [Fact]
    public async Task Add_MaximumTrimmedLengthsAndZeroPrice_AcceptsBoundary()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var url = "https://shop.example/" + new string('x', 2000 - "https://shop.example/".Length);
        var card = await store.Service.AddItemAsync(owner.Id, new()
        {
            Url = " " + url + " ", Name = " " + new string('n', 500) + " ", ImageUrl = url, Price = 0
        });
        Assert.Equal(2000, card.Url.Length);
        Assert.Equal(500, card.Name.Length);
        Assert.Equal(url, card.ImageUrl);
        Assert.Equal(0, card.Price);
    }

    [Fact]
    public async Task GetItems_ManualOrdering_ReturnsSortedSnapshots()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var first = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/a", Name = "A" });
        var second = await store.Service.AddItemAsync(owner.Id, new() { Url = "https://shop.example/b", Name = "B" });
        Assert.Equal(2, second.SortOrder);
        await store.Service.UpdateItemAsync(owner.Id, second.Id, new() { Url = second.Url, Name = second.Name, SortOrder = -1 });
        Assert.Equal(new[] { second.Id, first.Id }, (await store.Service.GetItemsAsync(owner.Id, owner.Id)).Select(x => x.Id));
    }

    [Theory]
    [InlineData("https://www.wildberries.ru/catalog/12", ProductSource.Wildberries)]
    [InlineData("https://wb.ru/12", ProductSource.Wildberries)]
    [InlineData("https://ozon.com/product/12", ProductSource.Ozon)]
    [InlineData("https://market.yandex.ru/product/12?sku=4", ProductSource.YandexMarket)]
    [InlineData("https://ozon.ru.evil.example/product/12", ProductSource.Unknown)]
    [InlineData("https://evilozon.ru/product/12", ProductSource.Unknown)]
    public void Source_ExactHostBoundary_DoesNotTrustHostSubstring(string url, ProductSource source)
    {
        Assert.Equal(source, WishlistValues.GetSource(url));
    }

    [Fact]
    public async Task Add_CancelledToken_DoesNotPersist()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Service.AddItemAsync(owner.Id,
            new() { Url = "https://shop.example/cancel", Name = "Cancel" }, cts.Token));
        Assert.Empty(await store.Db.WishlistItems.ToListAsync());
    }

    [Fact]
    public void NotificationTypes_LegacyValues_RemainStable()
    {
        Assert.Equal(0, (int)NotificationType.PriceChanged);
        Assert.Equal(1, (int)NotificationType.FriendRequest);
        Assert.Equal(2, (int)NotificationType.FriendRequestAccepted);
        Assert.Equal(3, (int)NotificationType.ItemReserved);
        Assert.Equal(4, (int)NotificationType.NewMessage);
    }
}
