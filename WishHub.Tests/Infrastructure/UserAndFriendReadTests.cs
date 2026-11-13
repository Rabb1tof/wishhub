using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;
using WishHub.Infrastructure.Storage;

namespace WishHub.Tests.Infrastructure;

public sealed class UserAndFriendReadTests
{
    [Fact]
    public async Task SearchAndProfileReads_MoreResults_KeepOneQueryAndProtectWishlistCounts()
    {
        await using var store = new WishlistTestStore();
        await using var fixture = new Reads(options => options.UseSqlite(store.Connection), store.ImageRootPath);
        await VerifySearchAndProfileReads(fixture);
    }

    [Fact]
    public async Task FriendReads_MoreFriendsAndRequests_KeepTwoQueriesAndPreserveDirectionAndOrder()
    {
        await using var store = new WishlistTestStore();
        await using var fixture = new Reads(options => options.UseSqlite(store.Connection), store.ImageRootPath);
        await VerifyFriendReads(fixture);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ProfileAndFriendReads_Postgres_KeepFixedQueryCountsAndPreserveVisibility()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "profile_reads_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();

        try
        {
            var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
            await using var fixture = new Reads(options => options.UseNpgsql(scopedConnection,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)), Path.GetTempPath());
            await fixture.Db.Database.MigrateAsync();
            await VerifySearchAndProfileReads(fixture);
            await VerifyFriendReads(fixture);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifySearchAndProfileReads(Reads fixture)
    {
        var viewer = fixture.AddUser("owner", WishlistPrivacy.Private);
        var publicUser = fixture.AddUser("ALPHA-public", WishlistPrivacy.Public);
        await fixture.Db.SaveChangesAsync();

        Assert.Empty(await fixture.Read(() => fixture.Users.SearchUsersAsync("a", viewer.Id), 0));
        Assert.Empty(await fixture.Read(() => fixture.Users.SearchUsersAsync("missing", viewer.Id), 1));
        var initial = Assert.Single(await fixture.Read(() => fixture.Users.SearchUsersAsync("AlPhA", viewer.Id), 1));
        Assert.Equal(publicUser.Id, initial.Id);
        Assert.Equal(1, initial.WishlistItemCount);
        Assert.Null(initial.FriendshipStatus);

        var accepted = fixture.AddUser("friend", WishlistPrivacy.FriendsOnly);
        accepted.DisplayName = "Alpha friend";
        accepted.Bio = "A bio";
        accepted.AvatarUrl = "https://example.test/avatar.png";
        fixture.AddFriendship(accepted, viewer, FriendshipStatus.Accepted, 1);
        fixture.AddFriendship(viewer, accepted, FriendshipStatus.Pending, 2);
        var blocked = fixture.AddUser("blocked", WishlistPrivacy.FriendsOnly);
        blocked.Email = "Alpha-blocked@example.test";
        fixture.AddFriendship(viewer, blocked, FriendshipStatus.Accepted, 3);
        fixture.AddFriendship(blocked, viewer, FriendshipStatus.Blocked, 4);
        var pending = fixture.AddUser("alpha-pending", WishlistPrivacy.FriendsOnly);
        fixture.AddFriendship(viewer, pending, FriendshipStatus.Pending, 5);
        var unrelated = fixture.AddUser("alpha-unrelated", WishlistPrivacy.FriendsOnly);
        var privateUser = fixture.AddUser("alpha-private", WishlistPrivacy.Private);
        fixture.AddFriendship(privateUser, viewer, FriendshipStatus.Accepted, 6);
        var invalid = fixture.AddUser("alpha-invalid", (WishlistPrivacy)42);
        fixture.AddFriendship(viewer, invalid, FriendshipStatus.Accepted, 7);
        await fixture.Db.SaveChangesAsync();

        var profiles = (await fixture.Read(() => fixture.Users.SearchUsersAsync("Alpha", viewer.Id), 1))
            .ToDictionary(profile => profile.Id);
        Assert.Equal(7, profiles.Count);
        Assert.Equal("friend", profiles[accepted.Id].Username);
        Assert.Equal("Alpha friend", profiles[accepted.Id].DisplayName);
        Assert.Equal("A bio", profiles[accepted.Id].Bio);
        Assert.Equal("https://example.test/avatar.png", profiles[accepted.Id].AvatarUrl);
        Assert.Equal(accepted.CreatedAt, profiles[accepted.Id].CreatedAt);
        Assert.Equal("FriendsOnly", profiles[accepted.Id].WishlistPrivacy);
        Assert.Equal(FriendshipStatus.Accepted, profiles[accepted.Id].FriendshipStatus);
        Assert.Equal(1, profiles[accepted.Id].WishlistItemCount);
        Assert.Equal(FriendshipStatus.Blocked, profiles[blocked.Id].FriendshipStatus);
        Assert.Equal(0, profiles[blocked.Id].WishlistItemCount);
        Assert.Equal(FriendshipStatus.Pending, profiles[pending.Id].FriendshipStatus);
        Assert.Equal(0, profiles[pending.Id].WishlistItemCount);
        Assert.Null(profiles[unrelated.Id].FriendshipStatus);
        Assert.Equal(0, profiles[unrelated.Id].WishlistItemCount);
        Assert.Equal(FriendshipStatus.Accepted, profiles[privateUser.Id].FriendshipStatus);
        Assert.Equal(0, profiles[privateUser.Id].WishlistItemCount);
        Assert.Equal(0, profiles[invalid.Id].WishlistItemCount);

        var anonymous = (await fixture.Read(() => fixture.Users.SearchUsersAsync("Alpha", null), 1)).ToDictionary(p => p.Id);
        Assert.Equal(1, anonymous[publicUser.Id].WishlistItemCount);
        Assert.All(anonymous.Values, profile => Assert.Null(profile.FriendshipStatus));
        Assert.All(anonymous.Values.Where(profile => profile.Id != publicUser.Id),
            profile => Assert.Equal(0, profile.WishlistItemCount));

        Assert.Empty(await fixture.Read(() => fixture.Users.GetProfilesByIdsAsync([], viewer.Id), 0));
        var batch = await fixture.Read(() => fixture.Users.GetProfilesByIdsAsync(
            [viewer.Id, accepted.Id, accepted.Id, Guid.NewGuid()], viewer.Id), 1);
        Assert.Equal(2, batch.Count);
        Assert.Null(batch[viewer.Id].FriendshipStatus);
        Assert.Equal(1, batch[viewer.Id].WishlistItemCount);
        Assert.Equal(1, batch[accepted.Id].WishlistItemCount);
        var single = await fixture.Read(() => fixture.Users.GetProfileAsync("friend", viewer.Id), 1);
        Assert.NotNull(single);
        Assert.Equal(batch[accepted.Id].FriendshipStatus, single.FriendshipStatus);
        Assert.Equal(batch[accepted.Id].WishlistItemCount, single.WishlistItemCount);

        for (var i = 0; i < 20; i++)
            fixture.AddUser($"alpha-cap-{i}", WishlistPrivacy.Public);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(20, (await fixture.Read(() => fixture.Users.SearchUsersAsync("Alpha", viewer.Id), 1)).Count);
    }

    private static async Task VerifyFriendReads(Reads fixture)
    {
        var viewer = fixture.AddUser("list-owner", WishlistPrivacy.Private);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Read(() => fixture.Friends.GetFriendsAsync(viewer.Id), 1));
        Assert.Empty(await fixture.Read(() => fixture.Friends.GetIncomingRequestsAsync(viewer.Id), 1));
        Assert.Empty(await fixture.Read(() => fixture.Friends.GetOutgoingRequestsAsync(viewer.Id), 1));

        var first = fixture.AddUser("list-first", WishlistPrivacy.FriendsOnly);
        var incoming = fixture.AddUser("list-incoming", WishlistPrivacy.Public);
        var outgoing = fixture.AddUser("list-outgoing", WishlistPrivacy.FriendsOnly);
        var firstRow = fixture.AddFriendship(viewer, first, FriendshipStatus.Accepted, 1);
        var incomingRow = fixture.AddFriendship(incoming, viewer, FriendshipStatus.Pending, 2);
        var outgoingRow = fixture.AddFriendship(viewer, outgoing, FriendshipStatus.Pending, 3);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(firstRow.Id, Assert.Single(await fixture.Read(() => fixture.Friends.GetFriendsAsync(viewer.Id), 2)).Id);
        Assert.Equal(incomingRow.Id, Assert.Single(await fixture.Read(() => fixture.Friends.GetIncomingRequestsAsync(viewer.Id), 2)).Id);
        Assert.Equal(outgoingRow.Id, Assert.Single(await fixture.Read(() => fixture.Friends.GetOutgoingRequestsAsync(viewer.Id), 2)).Id);

        var privateFriend = fixture.AddUser("list-private", WishlistPrivacy.Private);
        var unrelated = fixture.AddUser("list-unrelated", WishlistPrivacy.Public);
        var reverse = fixture.AddFriendship(first, viewer, FriendshipStatus.Accepted, 4);
        var privateRow = fixture.AddFriendship(privateFriend, viewer, FriendshipStatus.Accepted, 5);
        // Accepted list sorts by update time, request lists sort by creation time.
        privateRow.UpdatedAt = reverse.UpdatedAt!.Value.AddDays(1);
        privateRow.CreatedAt = firstRow.CreatedAt.AddDays(-1);
        var incomingLatest = fixture.AddFriendship(outgoing, viewer, FriendshipStatus.Pending, 6);
        var outgoingLatest = fixture.AddFriendship(viewer, incoming, FriendshipStatus.Pending, 7);
        fixture.AddFriendship(privateFriend, unrelated, FriendshipStatus.Pending, 8);
        fixture.AddFriendship(viewer, unrelated, FriendshipStatus.Blocked, 9);
        await fixture.Db.SaveChangesAsync();

        var friends = await fixture.Read(() => fixture.Friends.GetFriendsAsync(viewer.Id), 2);
        Assert.Collection(friends,
            entry =>
            {
                Assert.Equal(privateRow.Id, entry.Id);
                Assert.Equal(privateFriend.Id, entry.User.Id);
                Assert.Equal(privateRow.CreatedAt, entry.CreatedAt);
                Assert.Equal("Accepted", entry.Status);
                Assert.Equal(0, entry.User.WishlistItemCount);
            },
            entry =>
            {
                Assert.Equal(reverse.Id, entry.Id);
                Assert.Equal(first.Id, entry.User.Id);
                Assert.Equal(1, entry.User.WishlistItemCount);
            },
            entry =>
            {
                Assert.Equal(firstRow.Id, entry.Id);
                Assert.Equal(first.Id, entry.User.Id);
                Assert.Equal(1, entry.User.WishlistItemCount);
            });
        var incomingRequests = await fixture.Read(() => fixture.Friends.GetIncomingRequestsAsync(viewer.Id), 2);
        Assert.Equal(new[] { incomingLatest.Id, incomingRow.Id }, incomingRequests.Select(request => request.Id));
        Assert.Equal(new[] { outgoing.Id, incoming.Id }, incomingRequests.Select(request => request.From.Id));
        Assert.Equal(new[] { incomingLatest.CreatedAt, incomingRow.CreatedAt }, incomingRequests.Select(request => request.CreatedAt));
        Assert.Equal(new[] { 0, 1 }, incomingRequests.Select(request => request.From.WishlistItemCount));
        var outgoingRequests = await fixture.Read(() => fixture.Friends.GetOutgoingRequestsAsync(viewer.Id), 2);
        Assert.Equal(new[] { outgoingLatest.Id, outgoingRow.Id }, outgoingRequests.Select(request => request.Id));
        Assert.Equal(new[] { incoming.Id, outgoing.Id }, outgoingRequests.Select(request => request.From.Id));
        Assert.Equal(new[] { 1, 0 }, outgoingRequests.Select(request => request.From.WishlistItemCount));
        Assert.All(outgoingRequests, request => Assert.Equal(FriendshipStatus.Pending, request.From.FriendshipStatus));
    }

    private sealed class Reads : IAsyncDisposable
    {
        private static readonly DateTime Start = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly CommandCounter _counter = new();

        public Reads(Action<DbContextOptionsBuilder> configure, string avatarPath)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options =>
            {
                configure(options);
                options.AddInterceptors(_counter);
            });
            services.AddIdentityCore<User>().AddEntityFrameworkStores<AppDbContext>();
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            Db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Users = new UserService(_scope.ServiceProvider.GetRequiredService<UserManager<User>>(), Db,
                NullLogger<UserService>.Instance, new AvatarStorage(avatarPath));
            Friends = new FriendshipService(Db, Users, NullLogger<FriendshipService>.Instance);
        }

        public AppDbContext Db { get; }
        public UserService Users { get; }
        public FriendshipService Friends { get; }

        public User AddUser(string name, WishlistPrivacy privacy)
        {
            var user = new User { Id = Guid.NewGuid(), UserName = name, DisplayName = name, CreatedAt = Start, WishlistPrivacy = privacy };
            Db.Users.Add(user);
            Db.WishlistItems.Add(new WishlistItem
            {
                Id = Guid.NewGuid(), OwnerId = user.Id, Url = "https://example.test/gift", Name = "Gift", CreatedAt = Start
            });
            return user;
        }

        public Friendship AddFriendship(User requester, User addressee, FriendshipStatus status, int order)
        {
            var friendship = new Friendship
            {
                Id = Guid.NewGuid(), RequesterId = requester.Id, AddresseeId = addressee.Id, Status = status,
                CreatedAt = Start.AddMinutes(order), UpdatedAt = Start.AddMinutes(order)
            };
            Db.Friendships.Add(friendship);
            return friendship;
        }

        public async Task<T> Read<T>(Func<Task<T>> read, int expectedCommands)
        {
            _counter.Count = 0;
            var result = await read();
            Assert.Equal(expectedCommands, _counter.Count);
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database.";
        }
    }
}
