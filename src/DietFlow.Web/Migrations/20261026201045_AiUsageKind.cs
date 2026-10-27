using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietFlow.Web.Migrations
{
    /// <inheritdoc />
    public partial class AiUsageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAnalysisLogs_UserId_CreatedAt",
                table: "AiAnalysisLogs");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "AiAnalysisLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalysisLogs_UserId_Kind_CreatedAt",
                table: "AiAnalysisLogs",
                columns: new[] { "UserId", "Kind", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiAnalysisLogs_UserId_Kind_CreatedAt",
                table: "AiAnalysisLogs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AiAnalysisLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnalysisLogs_UserId_CreatedAt",
                table: "AiAnalysisLogs",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}
