using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleLanguageDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "detected_language",
                table: "embedded_subtitles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "detected_language_confidence",
                table: "embedded_subtitles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "language_checked_at",
                table: "embedded_subtitles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language_detection_evidence",
                table: "embedded_subtitles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language_detection_method",
                table: "embedded_subtitles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "language_detection_version",
                table: "embedded_subtitles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "detected_language",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "detected_language_confidence",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "language_checked_at",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "language_detection_evidence",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "language_detection_method",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "language_detection_version",
                table: "embedded_subtitles");
        }
    }
}
