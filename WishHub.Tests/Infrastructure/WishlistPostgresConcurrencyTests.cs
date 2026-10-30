using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WishHub.Core.DTOs.Wishlist;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Services;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class WishlistPostgresConcurrencyTests
{
    [PostgresFact]
    public async Task UpdateItem_ConcurrentFullReplacements_PersistsSecondRequestAndReturnsCurrentOrder()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "wishlist_put_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();

        var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        AppDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(scopedConnection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema));
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            return new AppDbContext(options.Options);
        }

        try
        {
            await using var setup = Context();
            await setup.Database.MigrateAsync();
            var owner = new User
            {
                Id = Guid.NewGuid(), UserName = "owner", Email = "owner@example.test",
                DisplayName = "Owner", CreatedAt = DateTime.UtcNow
            };
            setup.Users.Add(owner);
            await setup.SaveChangesAsync();
            var original = await new WishlistService(setup).AddItemAsync(owner.Id, new()
            {
                Url = "https://shop.example/original?sku=1", Name = "Original",
                ImageUrl = "https://cdn.example/original.jpg", Price = 10m, Currency = "RUB"
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var firstGate = new OwnerLockGate(holdAcquiredLock: true);
            var secondGate = new OwnerLockGate(holdAcquiredLock: false);
            await using var first = Context(firstGate);
            await using var second = Context(secondGate);
            var firstWrite = new WishlistService(first).UpdateItemAsync(owner.Id, original.Id, new()
            {
                Url = "https://www.ozon.ru/product/changed?sku=2", Name = "First",
                ImageUrl = "https://cdn.example/first.jpg", Price = 25m, Currency = "USD", SortOrder = 44
            }, timeout.Token);

            try
            {
                await firstGate.Acquired.Task.WaitAsync(timeout.Token);
                var secondWrite = new WishlistService(second).UpdateItemAsync(owner.Id, original.Id, new()
                {
                    Url = original.Url, Name = "Second",
                    ImageUrl = original.ImageUrl, Price = original.Price, Currency = original.Currency
                }, timeout.Token);
                await secondGate.Attempted.Task.WaitAsync(timeout.Token);
                firstGate.Release.TrySetResult();
                var responses = await Task.WhenAll(firstWrite, secondWrite);
                Assert.Equal("First", responses[0]!.Name);
                var response = Assert.IsType<WishlistItemDto>(responses[1]);

                await using var verification = Context();
                var saved = await verification.WishlistItems.AsNoTracking().SingleAsync(item => item.Id == original.Id);
                Assert.Equal("Second", saved.Name);
                Assert.Equal(original.Url, saved.Url);
                Assert.Equal(original.ImageUrl, saved.ImageUrl);
                Assert.Equal(original.Price, saved.Price);
                Assert.Equal(original.Currency, saved.Currency);
                Assert.Equal(ProductSource.Unknown, saved.Source);
                Assert.Equal(44, saved.SortOrder);
                Assert.Equal(saved.Url, response.Url);
                Assert.Equal(saved.Name, response.Name);
                Assert.Equal(saved.ImageUrl, response.ImageUrl);
                Assert.Equal(saved.Price, response.Price);
                Assert.Equal(saved.Currency, response.Currency);
                Assert.Equal(saved.Source.ToString(), response.Source);
                Assert.Equal(saved.SortOrder, response.SortOrder);
            }
            finally
            {
                firstGate.Release.TrySetResult();
                await firstWrite;
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database to run this test.";
        }
    }

    private sealed class OwnerLockGate(bool holdAcquiredLock) : DbCommandInterceptor
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Acquired.TrySetResult();
                if (holdAcquiredLock)
                    await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
