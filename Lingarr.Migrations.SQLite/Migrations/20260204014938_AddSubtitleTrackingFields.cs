using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_forced_subtitle",
                table: "translation_requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "selected_stream_title",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_subtitle_entry_count",
                table: "translation_requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_subtitle_type",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_forced_subtitle",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "selected_stream_title",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_subtitle_entry_count",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_subtitle_type",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "translation_requests");
        }
    }
}
