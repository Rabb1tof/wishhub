using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WishHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParsingStatusToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParseAttempts",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ParsingError",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParsingStatus",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParseAttempts",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ParsingError",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ParsingStatus",
                table: "Products");
        }
    }
}
