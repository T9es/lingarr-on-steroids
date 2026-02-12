using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_subtitle_check_at",
                table: "movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_subtitle_check_at",
                table: "episodes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_subtitle_check_at",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "last_subtitle_check_at",
                table: "episodes");
        }
    }
}
