using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WishHub.Infrastructure.Migrations;

/// <summary>Copies shared product data into independent wishlist snapshots without deleting legacy data.</summary>
public partial class AddWishlistItemSnapshots : Migration
{
    // Match .NET 9 String.Trim/Char.IsWhiteSpace without relying on the database locale.
    private const string TrimCharacters = """U&'\0009\000A\000B\000C\000D\0020\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'""";

    private const string NormalizedUrl = $$"""
        regexp_replace(btrim(p."Url", {{TrimCharacters}}), '^[A-Za-z][A-Za-z0-9+.-]*://[^/?#]+',
            lower(substring(btrim(p."Url", {{TrimCharacters}}) from '^[A-Za-z][A-Za-z0-9+.-]*://[^/?#]+')))
        """;

    private const string SnapshotName = $$"""
        COALESCE(NULLIF(btrim(w."CustomName", {{TrimCharacters}}), ''),
            CASE WHEN lower(btrim(p."Name", {{TrimCharacters}})) NOT IN ('загрузка...', 'загрузка…', 'loading...', 'loading…')
                THEN NULLIF(btrim(p."Name", {{TrimCharacters}}), '') END,
            'Товар с ' || COALESCE(NULLIF(lower(substring(btrim(p."Url", {{TrimCharacters}}) from '^[A-Za-z][A-Za-z0-9+.-]*://([^/?#:]+)')), ''), 'неизвестного сайта'))
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_WishlistItems_Products_ProductId", "WishlistItems");
        migrationBuilder.AlterColumn<Guid>(
            name: "ProductId", table: "WishlistItems", type: "uuid", nullable: true,
            oldClrType: typeof(Guid), oldType: "uuid");

        migrationBuilder.AddColumn<string>("Url", "WishlistItems", "character varying(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>("Name", "WishlistItems", "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<string>("ImageUrl", "WishlistItems", "character varying(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<decimal>("Price", "WishlistItems", "numeric", nullable: true);
        migrationBuilder.AddColumn<string>("Currency", "WishlistItems", "character varying(10)", maxLength: 10, nullable: true);
        migrationBuilder.AddColumn<int>("Source", "WishlistItems", "integer", nullable: true);

        // Deliberately keep CustomName and the old user setting until a separately reviewed cleanup.
        migrationBuilder.Sql($$"""
            LOCK TABLE "Products" IN SHARE ROW EXCLUSIVE MODE;

            DO $$
            DECLARE fallback_names bigint;
            BEGIN
                SELECT count(*) INTO fallback_names
                FROM "WishlistItems" w JOIN "Products" p ON p."Id" = w."ProductId"
                WHERE NULLIF(btrim(w."CustomName", {{TrimCharacters}}), '') IS NULL
                  AND (NULLIF(btrim(p."Name", {{TrimCharacters}}), '') IS NULL
                       OR lower(btrim(p."Name", {{TrimCharacters}})) IN ('загрузка...', 'загрузка…', 'loading...', 'loading…'));
                RAISE NOTICE 'Wishlist snapshot migration: % names replaced by domain placeholders', fallback_names;
            END $$;

            UPDATE "WishlistItems" w
            SET "Url" = {{NormalizedUrl}},
                "Name" = {{SnapshotName}},
                "ImageUrl" = p."ImageUrl",
                "Price" = p."Price",
                "Currency" = COALESCE(NULLIF(btrim(p."Currency", {{TrimCharacters}}), ''), 'RUB'),
                "Source" = p."Source"
            FROM "Products" p
            WHERE p."Id" = w."ProductId";

            DO $$
            DECLARE duplicate_groups bigint;
            BEGIN
                SELECT count(*) INTO duplicate_groups FROM (
                    SELECT "OwnerId", "Url" FROM "WishlistItems"
                    GROUP BY "OwnerId", "Url" HAVING count(*) > 1
                ) duplicates;
                RAISE NOTICE 'Wishlist snapshot migration: % duplicate owner/URL groups retained for manual review', duplicate_groups;
            END $$;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "Url", table: "WishlistItems", type: "character varying(2000)", maxLength: 2000, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(2000)", oldMaxLength: 2000, oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "Name", table: "WishlistItems", type: "character varying(500)", maxLength: 500, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(500)", oldMaxLength: 500, oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "Currency", table: "WishlistItems", type: "character varying(10)", maxLength: 10, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(10)", oldMaxLength: 10, oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "Source", table: "WishlistItems", type: "integer", nullable: false,
            oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "PriceRefreshIntervalHours", table: "AspNetUsers", type: "integer", nullable: false, defaultValue: 24,
            oldClrType: typeof(int), oldType: "integer");

        migrationBuilder.AddForeignKey(
            "FK_WishlistItems_Products_ProductId", "WishlistItems", "ProductId", "Products", principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old shared-product schema cannot represent independently edited snapshots.
        // Abort before any destructive operation; restore from a reviewed backup/export instead.
        migrationBuilder.Sql($$"""
            LOCK TABLE "WishlistItems", "Products" IN SHARE ROW EXCLUSIVE MODE;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "WishlistItems" w
                    LEFT JOIN "Products" p ON p."Id" = w."ProductId"
                    WHERE p."Id" IS NULL
                       OR w."Url" IS DISTINCT FROM {{NormalizedUrl}}
                       OR w."ImageUrl" IS DISTINCT FROM p."ImageUrl"
                       OR w."Price" IS DISTINCT FROM p."Price"
                       OR w."Currency" IS DISTINCT FROM COALESCE(NULLIF(btrim(p."Currency", {{TrimCharacters}}), ''), 'RUB')
                       OR w."Source" IS DISTINCT FROM p."Source"
                ) THEN
                    RAISE EXCEPTION 'Cannot roll back wishlist snapshots: independent cards or edited product fields would be lost. Export the new data and use a reviewed backup/restore procedure.';
                END IF;
            END $$;

            UPDATE "WishlistItems" w SET "CustomName" = w."Name"
            FROM "Products" p
            WHERE p."Id" = w."ProductId" AND w."Name" IS DISTINCT FROM {{SnapshotName}};
            """);

        migrationBuilder.DropForeignKey("FK_WishlistItems_Products_ProductId", "WishlistItems");
        migrationBuilder.DropColumn("Url", "WishlistItems");
        migrationBuilder.DropColumn("Name", "WishlistItems");
        migrationBuilder.DropColumn("ImageUrl", "WishlistItems");
        migrationBuilder.DropColumn("Price", "WishlistItems");
        migrationBuilder.DropColumn("Currency", "WishlistItems");
        migrationBuilder.DropColumn("Source", "WishlistItems");
        migrationBuilder.AlterColumn<Guid>(
            name: "ProductId", table: "WishlistItems", type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        migrationBuilder.AddForeignKey(
            "FK_WishlistItems_Products_ProductId", "WishlistItems", "ProductId", "Products", principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
