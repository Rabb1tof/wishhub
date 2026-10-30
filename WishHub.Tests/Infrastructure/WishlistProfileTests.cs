using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

public class WishlistProfileTests
{
    [Fact]
    public async Task Profile_ReadAndUpdate_RemovesRefreshSettingWhilePreservingProfileBehavior()
    {
        await using var store = new WishlistTestStore();
        var owner = await store.AddUserAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(store.Connection));
        services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new UserService(scope.ServiceProvider.GetRequiredService<UserManager<User>>(), db,
            NullLogger<UserService>.Instance, new ConfigurationBuilder().Build());
        var updated = await service.UpdateProfileAsync(owner.Id, new UpdateProfileRequest
        {
            DisplayName = "Edited owner", Bio = "My gifts", WishlistPrivacy = "Private"
        });
        Assert.Equal("Edited owner", updated!.DisplayName);
        Assert.Equal("My gifts", updated.Bio);
        Assert.Equal("Private", updated.WishlistPrivacy);
        var fetched = await service.GetProfileByIdAsync(owner.Id, owner.Id);
        Assert.Equal(updated.DisplayName, fetched!.DisplayName);
        var json = JsonSerializer.SerializeToElement(fetched, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("priceRefreshIntervalHours", out _));
        Assert.False(JsonSerializer.SerializeToElement(new UpdateProfileRequest(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .TryGetProperty("priceRefreshIntervalHours", out _));
        Assert.Equal(24, await db.Users.Where(u => u.Id == owner.Id)
            .Select(u => EF.Property<int>(u, "PriceRefreshIntervalHours")).SingleAsync());
    }
}
