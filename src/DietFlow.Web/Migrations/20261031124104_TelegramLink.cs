using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DietFlow.Web.Migrations
{
    /// <inheritdoc />
    public partial class TelegramLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLinkCode",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramLinkExpiresAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TelegramChatId",
                table: "AspNetUsers",
                column: "TelegramChatId",
                unique: true,
                filter: "\"TelegramChatId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TelegramLinkCode",
                table: "AspNetUsers",
                column: "TelegramLinkCode",
                unique: true,
                filter: "\"TelegramLinkCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TelegramChatId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TelegramLinkCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramLinkCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramLinkExpiresAt",
                table: "AspNetUsers");
        }
    }
}
