using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WishHub.Infrastructure.Migrations;

public partial class IndexRefreshTokenExpiry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_RefreshTokens_ExpiresAt_Id", table: "RefreshTokens", columns: new[] { "ExpiresAt", "Id" });

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropIndex(
        name: "IX_RefreshTokens_ExpiresAt_Id", table: "RefreshTokens");
}
