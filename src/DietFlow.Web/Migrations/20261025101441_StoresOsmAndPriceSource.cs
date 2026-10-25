using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietFlow.Web.Migrations
{
    /// <inheritdoc />
    public partial class StoresOsmAndPriceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OsmRef",
                table: "Stores",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "StorePrices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Latitude_Longitude",
                table: "Stores",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_OsmRef",
                table: "Stores",
                column: "OsmRef",
                unique: true,
                filter: "\"OsmRef\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stores_Latitude_Longitude",
                table: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Stores_OsmRef",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "OsmRef",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "StorePrices");
        }
    }
}
