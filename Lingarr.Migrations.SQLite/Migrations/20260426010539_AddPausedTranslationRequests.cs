using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddPausedTranslationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pause_reason",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paused_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paused_provider",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pause_reason",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "paused_provider",
                table: "translation_requests");
        }
    }
}
