using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietFlow.Web.Migrations
{
    /// <inheritdoc />
    public partial class ProductSortRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortRank",
                table: "Products",
                type: "integer",
                nullable: false,
                computedColumnSql: "CASE WHEN \"Barcode\" IS NULL THEN 0 WHEN \"Name\" ~ '[А-Яа-яЁёЇїІіЄєҐґ]' THEN 1 ELSE 2 END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SortRank_Name_Id",
                table: "Products",
                columns: new[] { "SortRank", "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_SortRank_Name_Id",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SortRank",
                table: "Products");
        }
    }
}
