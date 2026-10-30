using Microsoft.EntityFrameworkCore;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Validation;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

public sealed class WishlistImageServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7nwAAAAASUVORK5CYII=");

    [Fact]
    public async Task Upload_Cleanup_UsesCurrentCardReferencesAndKeepsOtherOwnersFiles()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var other = await store.AddUserAsync();
        var service = new WishlistImageService(store.Db, store.Service, store.Images);
        async Task<ManagedWishlistImage> Upload(Guid id)
        {
            using var content = new MemoryStream(Png);
            var url = await service.UploadAsync(id, content, "image/png");
            Assert.True(ManagedWishlistImage.TryParse(url, out var image));
            return image;
        }
        var kept = await Upload(owner.Id);
        var orphan = await Upload(owner.Id);
        var otherOwner = await Upload(other.Id);
        foreach (var image in new[] { kept, orphan, otherOwner })
            File.SetLastWriteTimeUtc(Path.Combine(store.ImageRootPath, image.OwnerId.ToString("D"),
                $"{image.ImageId:D}.{image.Extension}"), DateTime.UtcNow.AddHours(-25));

        var card = await store.Service.AddItemAsync(owner.Id, new()
        {
            Url = "https://shop.example/gift", Name = "Saved image", ImageUrl = kept.Url
        });
        var recent = await Upload(owner.Id);
        Assert.True(store.Images.Exists(kept));
        Assert.False(store.Images.Exists(orphan));
        Assert.True(store.Images.Exists(otherOwner));
        Assert.True(store.Images.Exists(recent));
        Assert.Equal(kept.Url, (await store.Db.WishlistItems.AsNoTracking().SingleAsync()).ImageUrl);

        await store.Service.UpdateItemAsync(owner.Id, card.Id, new UpdateWishlistItemRequest
        {
            Url = card.Url, Name = card.Name, ImageUrl = null
        });
        await Upload(owner.Id);
        Assert.False(store.Images.Exists(kept));
        Assert.True(store.Images.Exists(recent));
        Assert.True(store.Images.Exists(otherOwner));
        Assert.Null((await store.Db.WishlistItems.AsNoTracking().SingleAsync()).ImageUrl);
    }
}
