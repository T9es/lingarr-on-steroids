using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleOcrSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ocr_approved_at",
                table: "embedded_subtitles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ocr_attempted_at",
                table: "embedded_subtitles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ocr_completed_at",
                table: "embedded_subtitles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ocr_cue_count",
                table: "embedded_subtitles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ocr_error",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ocr_extracted_path",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ocr_issue_summary",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ocr_quality_score",
                table: "embedded_subtitles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ocr_status",
                table: "embedded_subtitles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ocr_approved_at",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_attempted_at",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_completed_at",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_cue_count",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_error",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_extracted_path",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_issue_summary",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_quality_score",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "ocr_status",
                table: "embedded_subtitles");
        }
    }
}
