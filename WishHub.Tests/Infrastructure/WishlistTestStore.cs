using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

internal sealed class WishlistTestStore : IAsyncDisposable
{
    public SqliteConnection Connection { get; } = new("Data Source=:memory:");
    public AppDbContext Db { get; }
    public WishlistService Service { get; }

    public WishlistTestStore()
    {
        Connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
        Db.Database.EnsureCreated();
        Service = new WishlistService(Db);
    }

    public async Task<User> AddUserAsync(WishlistPrivacy privacy = WishlistPrivacy.Public)
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id, UserName = "user-" + id, Email = id + "@example.test", SecurityStamp = Guid.NewGuid().ToString(),
            DisplayName = "Wishlist user", WishlistPrivacy = privacy, CreatedAt = DateTime.UtcNow
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
