using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
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
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "detected_language_confidence",
                table: "embedded_subtitles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "language_checked_at",
                table: "embedded_subtitles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language_detection_evidence",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language_detection_method",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "language_detection_version",
                table: "embedded_subtitles",
                type: "integer",
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
