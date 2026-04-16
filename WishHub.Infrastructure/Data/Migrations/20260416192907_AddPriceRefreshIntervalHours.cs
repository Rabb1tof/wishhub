using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WishHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceRefreshIntervalHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceRefreshIntervalHours",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 24);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceRefreshIntervalHours",
                table: "AspNetUsers");
        }
    }
}
