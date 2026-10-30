using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using WishHub.Core.DTOs.Users;
using WishHub.Core.Entities;
using WishHub.Core.Interfaces;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class FriendshipSecurityTests
{
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendRequest_ConcurrentPair_PersistsOneRequestAndOneNotification(bool reciprocal)
    {
        await using var store = await Store.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstGate = new SaveGate(hold: true);
        var secondGate = new SaveGate(hold: false);
        var secondLock = new LockAttempt();
        await using var first = store.Context(firstGate);
        await using var second = store.Context(secondGate, secondLock);
        var firstSend = Service(first).SendRequestAsync(store.A, store.B, timeout.Token);
        await firstGate.Entered.Task.WaitAsync(timeout.Token);
        var secondSend = Service(second).SendRequestAsync(reciprocal ? store.B : store.A,
            reciprocal ? store.A : store.B, timeout.Token);
        await CompleteConcurrent(firstSend, secondSend, firstGate, secondGate, secondLock, timeout.Token);

        Assert.Single(new[] { await firstSend, await secondSend }, result => result != null);
        await using var verify = store.Context();
        var friendship = Assert.Single(await verify.Friendships.AsNoTracking().ToListAsync());
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
        Assert.Equal(store.A, friendship.RequesterId);
        Assert.Equal(store.B, friendship.AddresseeId);
        var notification = Assert.Single(await verify.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(NotificationType.FriendRequest, notification.Type);
        Assert.Equal(store.B, notification.UserId);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveFriend_LegacyReverseRows_RevokesPairOnly(bool reversePending)
    {
        await using var store = await Store.CreateAsync();
        var (_, reverse) = await store.SeedPairAsync(reversePending);
        await using var db = store.Context();
        var unrelated = store.Row(store.A, store.C, FriendshipStatus.Accepted);
        db.Friendships.Add(unrelated);
        await db.SaveChangesAsync();
        Assert.True(await new WishlistService(db, null!).CanViewWishlistAsync(store.B, store.A));

        Assert.True(await Service(db).RemoveFriendAsync(store.A, store.B));

        await using var verify = store.Context();
        Assert.Equal(unrelated.Id, (await verify.Friendships.SingleAsync()).Id);
        Assert.False(await new WishlistService(verify, null!).CanViewWishlistAsync(store.B, store.A));
        Assert.Null(await Service(verify).AcceptRequestAsync(reverse, store.A));
        Assert.True(await new WishlistService(verify, null!).CanViewWishlistAsync(store.C, store.A));
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveFriend_ConcurrentAccept_DoesNotRestoreAccess(bool acceptFirst)
    {
        await using var store = await Store.CreateAsync();
        var (_, pending) = await store.SeedPairAsync(reversePending: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstGate = new SaveGate(hold: true);
        var secondGate = new SaveGate(hold: false);
        var secondLock = new LockAttempt();
        await using var first = store.Context(firstGate);
        await using var second = store.Context(secondGate, secondLock);
        var accepting = Service(acceptFirst ? first : second);
        var removing = Service(acceptFirst ? second : first);
        Task<WishHub.Core.DTOs.Friends.FriendshipDto?>? accept = null;
        Task<bool>? remove = null;
        var firstOperation = acceptFirst
            ? (Task)(accept = accepting.AcceptRequestAsync(pending, store.A, timeout.Token))
            : remove = removing.RemoveFriendAsync(store.A, store.B, timeout.Token);
        await firstGate.Entered.Task.WaitAsync(timeout.Token);
        var secondOperation = acceptFirst
            ? (Task)(remove = removing.RemoveFriendAsync(store.A, store.B, timeout.Token))
            : accept = accepting.AcceptRequestAsync(pending, store.A, timeout.Token);
        await CompleteConcurrent(firstOperation, secondOperation, firstGate, secondGate, secondLock, timeout.Token);

        Assert.True(await remove!);
        if (acceptFirst) Assert.NotNull(await accept!);
        else Assert.Null(await accept!);
        await using var verify = store.Context();
        Assert.Empty(await verify.Friendships.ToListAsync());
        Assert.False(await new WishlistService(verify, null!).CanViewWishlistAsync(store.B, store.A));
        Assert.Equal(acceptFirst ? 1 : 0, await verify.Notifications.CountAsync(n => n.Type == NotificationType.FriendRequestAccepted));
    }

    [PostgresFact]
    public async Task AcceptRequest_ConcurrentAttempts_CreatesSingleAcceptanceNotification()
    {
        await using var store = await Store.CreateAsync();
        var pending = await store.SeedPendingAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstGate = new SaveGate(hold: true);
        var secondGate = new SaveGate(hold: false);
        var secondLock = new LockAttempt();
        await using var first = store.Context(firstGate);
        await using var second = store.Context(secondGate, secondLock);
        var firstAccept = Service(first).AcceptRequestAsync(pending, store.B, timeout.Token);
        await firstGate.Entered.Task.WaitAsync(timeout.Token);
        var secondAccept = Service(second).AcceptRequestAsync(pending, store.B, timeout.Token);
        await CompleteConcurrent(firstAccept, secondAccept, firstGate, secondGate, secondLock, timeout.Token);

        Assert.Single(new[] { await firstAccept, await secondAccept }, result => result != null);
        await using var verify = store.Context();
        Assert.Equal(FriendshipStatus.Accepted, (await verify.Friendships.SingleAsync()).Status);
        var notification = Assert.Single(await verify.Notifications.ToListAsync());
        Assert.Equal(NotificationType.FriendRequestAccepted, notification.Type);
        Assert.Equal(store.A, notification.UserId);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectOrCancel_ConcurrentAccept_RemovesRequest(bool acceptFirst)
    {
        await using var store = await Store.CreateAsync();
        var pending = await store.SeedPendingAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstGate = new SaveGate(hold: true);
        var secondGate = new SaveGate(hold: false);
        var secondLock = new LockAttempt();
        await using var first = store.Context(firstGate);
        await using var second = store.Context(secondGate, secondLock);
        var accepting = Service(acceptFirst ? first : second);
        var cancelling = Service(acceptFirst ? second : first);
        Task<WishHub.Core.DTOs.Friends.FriendshipDto?>? accept = null;
        Task<bool>? cancel = null;
        var firstOperation = acceptFirst
            ? (Task)(accept = accepting.AcceptRequestAsync(pending, store.B, timeout.Token))
            : cancel = cancelling.RejectOrCancelRequestAsync(pending, store.A, timeout.Token);
        await firstGate.Entered.Task.WaitAsync(timeout.Token);
        var secondOperation = acceptFirst
            ? (Task)(cancel = cancelling.RejectOrCancelRequestAsync(pending, store.A, timeout.Token))
            : accept = accepting.AcceptRequestAsync(pending, store.B, timeout.Token);
        await CompleteConcurrent(firstOperation, secondOperation, firstGate, secondGate, secondLock, timeout.Token);

        Assert.True(await cancel!);
        if (acceptFirst) Assert.NotNull(await accept!);
        else Assert.Null(await accept!);
        await using var verify = store.Context();
        Assert.Empty(await verify.Friendships.ToListAsync());
        Assert.Equal(acceptFirst ? 1 : 0, await verify.Notifications.CountAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectOrCancel_LegacyPair_RevokesAcceptedButOnlyCancelsPending(bool cancelAccepted)
    {
        await using var store = await Store.CreateAsync();
        var (accepted, pending) = await store.SeedPairAsync(reversePending: true);
        await using var db = store.Context();
        var unrelated = store.Row(store.A, store.C, FriendshipStatus.Accepted);
        db.Friendships.Add(unrelated);
        await db.SaveChangesAsync();
        Assert.True(await Service(db).RejectOrCancelRequestAsync(cancelAccepted ? accepted : pending, store.A));
        await using var verify = store.Context();
        var surviving = await verify.Friendships.AsNoTracking().ToListAsync();
        Assert.Contains(surviving, f => f.Id == unrelated.Id);
        if (cancelAccepted)
            Assert.Single(surviving);
        else
            Assert.Equal(new[] { accepted, unrelated.Id }.Order(), surviving.Select(f => f.Id).Order());
        Assert.Equal(!cancelAccepted, await new WishlistService(verify, null!).CanViewWishlistAsync(store.B, store.A));
    }

    [PostgresFact]
    public async Task Mutations_UnrelatedUser_CannotAcceptCancelOrRemoveFriendship()
    {
        await using var store = await Store.CreateAsync();
        var pending = await store.SeedPendingAsync();
        await using var db = store.Context();
        var service = Service(db);
        Assert.Null(await service.AcceptRequestAsync(pending, store.C));
        Assert.False(await service.RejectOrCancelRequestAsync(pending, store.C));
        Assert.False(await service.RemoveFriendAsync(store.C, store.A));
        Assert.Equal(FriendshipStatus.Pending, (await db.Friendships.SingleAsync()).Status);
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    private static FriendshipService Service(AppDbContext db)
    {
        var users = Substitute.For<IUserService>();
        users.GetProfileByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserProfileDto { Id = call.ArgAt<Guid>(0), Username = "synthetic", DisplayName = "Synthetic" });
        return new(db, users, NullLogger<FriendshipService>.Instance);
    }

    private static async Task CompleteConcurrent(Task first, Task second, SaveGate firstGate,
        SaveGate secondGate, LockAttempt secondLock, CancellationToken ct)
    {
        try
        {
            // Baseline reaches its write; serialized code waits on the first transaction's row lock.
            await Task.WhenAny(secondGate.Entered.Task, secondLock.Attempted.Task, second).WaitAsync(ct);
        }
        finally
        {
            firstGate.Release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(ct);
        }
    }

    private sealed class Store(string connectionString, string schema) : IAsyncDisposable
    {
        public Guid A { get; } = Guid.NewGuid();
        public Guid B { get; } = Guid.NewGuid();
        public Guid C { get; } = Guid.NewGuid();

        public AppDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .AddInterceptors(interceptors).Options);

        public static async Task<Store> CreateAsync()
        {
            var store = new Store(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!, "friendship_security_" + Guid.NewGuid().ToString("N"));
            await using var admin = new NpgsqlConnection(store.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{store.Schema}\"", admin);
            await create.ExecuteNonQueryAsync();
            try
            {
                await using var db = store.Context();
                await db.Database.MigrateAsync();
                db.Users.AddRange(new[] { store.A, store.B, store.C }.Select(id => new User
                {
                    Id = id, UserName = "user_" + id.ToString("N"), DisplayName = "Synthetic", CreatedAt = DateTime.UtcNow,
                    WishlistPrivacy = WishlistPrivacy.FriendsOnly
                }));
                await db.SaveChangesAsync();
                return store;
            }
            catch { await store.DisposeAsync(); throw; }
        }

        private string ConnectionString => connectionString;
        private string Schema => schema;

        public Friendship Row(Guid requester, Guid addressee, FriendshipStatus status) => new()
        { Id = Guid.NewGuid(), RequesterId = requester, AddresseeId = addressee, Status = status, CreatedAt = DateTime.UtcNow };

        public async Task<(Guid Accepted, Guid Reverse)> SeedPairAsync(bool reversePending)
        {
            await using var db = Context();
            var accepted = Row(A, B, FriendshipStatus.Accepted);
            var reverse = Row(B, A, reversePending ? FriendshipStatus.Pending : FriendshipStatus.Accepted);
            db.Friendships.AddRange(accepted, reverse);
            await db.SaveChangesAsync();
            return (accepted.Id, reverse.Id);
        }

        public async Task<Guid> SeedPendingAsync()
        {
            await using var db = Context();
            var pending = Row(A, B, FriendshipStatus.Pending);
            db.Friendships.Add(pending);
            await db.SaveChangesAsync();
            return pending.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SaveGate(bool hold) : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            if (hold) await Release.Task.WaitAsync(ct);
            return result;
        }
    }

    private sealed class LockAttempt : DbCommandInterceptor
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES"))) Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database."; }
    }

    private sealed class PostgresTheoryAttribute : TheoryAttribute
    {
        public PostgresTheoryAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES"))) Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database."; }
    }
}
