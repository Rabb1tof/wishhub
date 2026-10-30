using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WishHub.Infrastructure.Data;
using WishHub.Infrastructure.Migrations;

namespace WishHub.Tests.Infrastructure;

public class WishlistSnapshotMigrationTests
{
    private const string PreviousMigration = "20260418125302_AddParsingStatusToProduct";
    private const string SnapshotMigration = "20261030190000_AddWishlistItemSnapshots";

    [Fact]
    public void Up_LegacyData_PreservesTablesColumnsAndUnboundedNullablePrice()
    {
        var operations = new AddWishlistItemSnapshots().UpOperations;

        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<DeleteDataOperation>());
        var price = Assert.Single(operations.OfType<AddColumnOperation>(), operation => operation.Name == "Price");
        Assert.Equal("WishlistItems", price.Table);
        Assert.Equal("numeric", price.ColumnType);
        Assert.True(price.IsNullable);
        Assert.Null(price.Precision);
        Assert.Null(price.Scale);
        var image = Assert.Single(operations.OfType<AddColumnOperation>(), operation => operation.Name == "ImageUrl");
        Assert.True(image.IsNullable);
        Assert.Equal(2000, image.MaxLength);
    }

    [Fact]
    public void Up_Backfill_PopulatesSnapshotsBeforeRequiringThemAndRetainsDuplicateRows()
    {
        var operations = new AddWishlistItemSnapshots().UpOperations.ToList();
        var backfill = Assert.Single(operations.OfType<SqlOperation>());
        var backfillPosition = operations.IndexOf(backfill);
        var requiredColumns = operations.OfType<AlterColumnOperation>()
            .Where(operation => operation.Table == "WishlistItems" && !operation.IsNullable).ToArray();

        Assert.Equal(new[] { "Url", "Name", "Currency", "Source" }, requiredColumns.Select(operation => operation.Name));
        Assert.All(requiredColumns, operation => Assert.True(operations.IndexOf(operation) > backfillPosition));
        Assert.All(operations.OfType<AddColumnOperation>(), operation => Assert.True(operations.IndexOf(operation) < backfillPosition));
        Assert.Contains("\"ImageUrl\" = p.\"ImageUrl\"", backfill.Sql);
        Assert.Contains("\"Price\" = p.\"Price\"", backfill.Sql);
        Assert.Contains("COALESCE(NULLIF(btrim(w.\"CustomName\", U&'", backfill.Sql);
        Assert.Contains("'загрузка...'", backfill.Sql);
        Assert.Contains("'Товар с '", backfill.Sql);
        Assert.DoesNotContain("DELETE", backfill.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", backfill.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(operations.OfType<CreateIndexOperation>());
        Assert.Empty(operations.OfType<DropIndexOperation>());
    }

    [Fact]
    public void Up_LegacyProductDeleted_KeepsWishlistSnapshotWithNullableProductReference()
    {
        var operations = new AddWishlistItemSnapshots().UpOperations;
        var productId = Assert.Single(operations.OfType<AlterColumnOperation>(), operation => operation.Name == "ProductId");
        var productForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.True(productId.IsNullable);
        Assert.Equal(new[] { "ProductId" }, productForeignKey.Columns);
        Assert.Equal("Products", productForeignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.SetNull, productForeignKey.OnDelete);
    }

    [Fact]
    public void Down_IndependentData_GuardsBeforeDroppingFieldsAndPreservesNames()
    {
        var operations = new AddWishlistItemSnapshots().DownOperations;
        var guard = Assert.IsType<SqlOperation>(operations[0]);

        Assert.Contains("p.\"Id\" IS NULL", guard.Sql);
        foreach (var field in new[] { "Url", "ImageUrl", "Price", "Currency", "Source" })
            Assert.Contains($"w.\"{field}\" IS DISTINCT FROM", guard.Sql);
        Assert.Contains("LOCK TABLE \"WishlistItems\", \"Products\" IN SHARE ROW EXCLUSIVE MODE", guard.Sql);
        Assert.Contains("RAISE EXCEPTION", guard.Sql);
        Assert.Contains("SET \"CustomName\" = w.\"Name\"", guard.Sql);
        Assert.Contains("w.\"Name\" IS DISTINCT FROM COALESCE(NULLIF(btrim(w.\"CustomName\", U&'", guard.Sql);
        Assert.Equal(new[] { "Url", "Name", "ImageUrl", "Price", "Currency", "Source" },
            operations.OfType<DropColumnOperation>().Select(operation => operation.Name));
        Assert.Empty(operations.OfType<DropTableOperation>());
    }

    [Fact]
    public void Scripts_EveryTrim_UsesExactDotNetWhitespaceSet()
    {
        var expected = Enumerable.Range(0, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace).ToArray();
        Assert.Equal(25, expected.Length);
        var migration = new AddWishlistItemSnapshots();
        foreach (var sql in migration.UpOperations.Concat(migration.DownOperations).OfType<SqlOperation>().Select(operation => operation.Sql))
        {
            var trims = System.Text.RegularExpressions.Regex.Matches(sql, """btrim\([^,]+,\s*U&'(?<characters>[^']+)'\)""");
            Assert.NotEmpty(trims);
            Assert.Equal(System.Text.RegularExpressions.Regex.Matches(sql, @"\bbtrim\(").Count, trims.Count);
            foreach (System.Text.RegularExpressions.Match trim in trims)
            {
                var characters = System.Text.RegularExpressions.Regex.Matches(trim.Groups["characters"].Value, @"\\([0-9A-Fa-f]{4})")
                    .Select(match => (char)Convert.ToInt32(match.Groups[1].Value, 16)).ToArray();
                Assert.Equal(expected, characters);
            }
        }
    }

    [Fact]
    public void Scripts_PostgreSqlGeneration_UsesTransactionalGuardAndCopiesBeforeNotNull()
    {
        using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();

        var up = migrator.GenerateScript(PreviousMigration, SnapshotMigration);
        var down = migrator.GenerateScript(SnapshotMigration, PreviousMigration);

        Assert.Contains("START TRANSACTION;", up);
        Assert.Contains("ON DELETE SET NULL", up);
        Assert.True(up.IndexOf("UPDATE \"WishlistItems\" w", StringComparison.Ordinal)
            < up.IndexOf("ALTER COLUMN \"Name\" SET NOT NULL", StringComparison.Ordinal));
        Assert.Contains("START TRANSACTION;", down);
        Assert.True(down.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal)
            < down.IndexOf("DROP COLUMN \"Name\"", StringComparison.Ordinal));
        Assert.Contains("COMMIT;", down);
    }

    [Fact]
    public void Snapshot_CurrentModel_HasNoPendingSchemaChanges()
    {
        using var db = CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=wishlist_migration_sql_only;Username=unused;Password=unused")
        .Options);
}
