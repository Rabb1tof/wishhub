using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WishHub.Api.Authentication;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class RefreshTokenCleanupTests
{
    [PostgresFact]
    public async Task PruneExpiredAsync_MigratedPostgres_RemovesOldestBoundedBatchAndPreservesUnexpiredRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "session_expiry_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        try
        {
            await using var services = new ServiceCollection()
                .AddDbContext<AppDbContext>(options => options.UseNpgsql(scopedConnection,
                    pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)))
                .BuildServiceProvider();
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var migrator = db.GetService<IMigrator>();
            const string beforeIndex = "20261030190000_AddWishlistItemSnapshots";
            await migrator.MigrateAsync(beforeIndex);

            var now = DateTime.UtcNow;
            var owner = new User
            {
                Id = Guid.NewGuid(), UserName = "owner", Email = "owner@example.test",
                DisplayName = "Owner", CreatedAt = now.AddDays(-7)
            };
            db.Users.Add(owner);
            var expired = Enumerable.Range(0, 501).Select(index => new RefreshToken
            {
                Id = Guid.NewGuid(), UserId = owner.Id, Token = "local-expired-" + index,
                CreatedAt = now.AddDays(-7), ExpiresAt = now.AddMinutes(index - 502), IsRevoked = index % 2 == 0
            }).ToArray();
            var active = new[]
            {
                new RefreshToken { Id = Guid.NewGuid(), UserId = owner.Id, Token = "oauth.local-active", CreatedAt = now, ExpiresAt = now.AddMinutes(1) },
                new RefreshToken { Id = Guid.NewGuid(), UserId = owner.Id, Token = "local-active", CreatedAt = now, ExpiresAt = now.AddDays(1) },
                new RefreshToken { Id = Guid.NewGuid(), UserId = owner.Id, Token = "local-revoked", CreatedAt = now, ExpiresAt = now.AddDays(1), IsRevoked = true },
            };
            db.RefreshTokens.AddRange(expired.Concat(active));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await migrator.MigrateAsync();
            Assert.Contains("20261114120000_IndexRefreshTokenExpiry", await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(504, await db.RefreshTokens.CountAsync());
            var cleanup = new RefreshTokenCleanup(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RefreshTokenCleanup>.Instance);
            Assert.Equal(500, await cleanup.PruneExpiredAsync(default));
            Assert.Equal(expired[^1].Id, await db.RefreshTokens.Where(token => token.ExpiresAt < now).Select(token => token.Id).SingleAsync());
            Assert.Equal(1, await cleanup.PruneExpiredAsync(default));
            Assert.Equal(0, await cleanup.PruneExpiredAsync(default));
            Assert.Equal(active.Select(token => token.Id).Order(), (await db.RefreshTokens.Select(token => token.Id).ToListAsync()).Order());

            await migrator.MigrateAsync(beforeIndex);
            Assert.DoesNotContain("20261114120000_IndexRefreshTokenExpiry", await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(3, await db.RefreshTokens.CountAsync());
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
}
