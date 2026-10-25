using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietFlow.Web.Migrations
{
    /// <inheritdoc />
    public partial class OfficialPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegionCode",
                table: "Cities",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OfficialPrices",
                columns: table => new
                {
                    RegionCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    StatCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StatName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PricePerKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialPrices", x => new { x.RegionCode, x.ProductId });
                    table.ForeignKey(
                        name: "FK_OfficialPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialPrices_ProductId",
                table: "OfficialPrices",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficialPrices");

            migrationBuilder.DropColumn(
                name: "RegionCode",
                table: "Cities");
        }
    }
}
