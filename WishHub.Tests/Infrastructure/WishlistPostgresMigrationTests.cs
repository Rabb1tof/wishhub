using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WishHub.Core.Entities;
using WishHub.Infrastructure.Data;

namespace WishHub.Tests.Infrastructure;

[Collection("Postgres qualification")]
[Trait("Category", "Integration")]
public sealed class WishlistPostgresMigrationTests
{
    [PostgresFact]
    public async Task UpAndDown_AllDotNetWhitespace_MatchesRuntimeTrimAndPreservesLegacyValues()
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")!;
        var schema = "wishlist_trim_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(scopedConnection, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;

        try
        {
            await using var db = new AppDbContext(options);
            var migrator = db.GetService<IMigrator>();
            const string previousMigration = "20260418125302_AddParsingStatusToProduct";
            await migrator.MigrateAsync(previousMigration);
            var ownerId = Guid.NewGuid();
            var createdAt = new DateTime(2026, 4, 1, 10, 30, 15, DateTimeKind.Utc);
            db.Users.Add(new User
            {
                Id = ownerId, UserName = "owner", Email = "owner@example.test",
                DisplayName = "Owner", CreatedAt = createdAt
            });
            await db.SaveChangesAsync();

            var whitespace = Enumerable.Range(0, char.MaxValue + 1)
                .Select(value => (char)value).Where(char.IsWhiteSpace).ToArray();
            Assert.Equal(25, whitespace.Length);
            var fixtures = new List<LegacyFixture>();
            foreach (var character in whitespace)
            {
                var padding = character.ToString();
                for (var branch = 0; branch < 3; branch++)
                {
                    var host = $"whitespace-{(int)character:x4}-{branch}.example";
                    var product = new Product
                    {
                        Id = Guid.NewGuid(),
                        Url = padding + $"HTTPS://{host.ToUpperInvariant()}/Path?sku=Ab%2fCd&variant=4#Blue" + padding,
                        Name = padding + (branch == 2 ? "Загрузка..." : "Real product") + padding,
                        Currency = branch == 0 ? padding + "€" + padding : padding,
                        Source = ProductSource.Unknown, ParsingStatus = ParsingStatus.Pending,
                        CreatedAt = createdAt, LastParsedAt = createdAt
                    };
                    fixtures.Add(new LegacyFixture(product,
                        branch == 0 ? padding + "Manual name" + padding : padding,
                        $"https://{host}/Path?sku=Ab%2fCd&variant=4#Blue",
                        branch == 0 ? "Manual name" : branch == 1 ? "Real product" : "Товар с " + host,
                        branch == 0 ? "€" : "RUB"));
                }
            }
            db.Products.AddRange(fixtures.Select(fixture => fixture.Product));
            await db.SaveChangesAsync();
            foreach (var fixture in fixtures)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"WishlistItems\" (\"Id\", \"OwnerId\", \"ProductId\", \"CustomName\", \"IsReserved\", \"SortOrder\", \"CreatedAt\") VALUES ({Guid.NewGuid()}, {ownerId}, {fixture.Product.Id}, {fixture.CustomName}, {false}, {100}, {createdAt})");
            db.ChangeTracker.Clear();

            await migrator.MigrateAsync("20261030190000_AddWishlistItemSnapshots");
            var snapshots = await db.WishlistItems.AsNoTracking().ToDictionaryAsync(item => item.ProductId!.Value);
            Assert.Equal(75, snapshots.Count);
            foreach (var fixture in fixtures)
            {
                var snapshot = snapshots[fixture.Product.Id];
                Assert.Equal(fixture.ExpectedUrl, snapshot.Url);
                Assert.Equal(fixture.ExpectedName, snapshot.Name);
                Assert.Equal(fixture.ExpectedCurrency, snapshot.Currency);
                Assert.Null(snapshot.ImageUrl);
                Assert.Null(snapshot.Price);
            }

            await migrator.MigrateAsync(previousMigration);
            var restoredProducts = await db.Products.AsNoTracking().ToDictionaryAsync(product => product.Id);
            var restoredCustomNames = new Dictionary<Guid, string?>();
            await db.Database.OpenConnectionAsync();
            await using (var command = new NpgsqlCommand("SELECT \"ProductId\", \"CustomName\" FROM \"WishlistItems\"", (NpgsqlConnection)db.Database.GetDbConnection()))
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    restoredCustomNames.Add(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            Assert.Equal(75, restoredCustomNames.Count);
            foreach (var fixture in fixtures)
            {
                Assert.Equal(fixture.CustomName, restoredCustomNames[fixture.Product.Id]);
                var restoredProduct = restoredProducts[fixture.Product.Id];
                Assert.Equal(fixture.Product.Url, restoredProduct.Url);
                Assert.Equal(fixture.Product.Name, restoredProduct.Name);
                Assert.Equal(fixture.Product.Currency, restoredProduct.Currency);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed record LegacyFixture(Product Product, string CustomName,
        string ExpectedUrl, string ExpectedName, string ExpectedCurrency);

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WISHHUB_TEST_POSTGRES")))
                Skip = "Set WISHHUB_TEST_POSTGRES to a disposable PostgreSQL database to run this test.";
        }
    }
}
