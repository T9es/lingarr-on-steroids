using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddSubtitleOutputModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.AddColumn<string>(
                name: "generated_output_formats",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "required_output_formats",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_subtitle_format",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitle_output_mode",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "target_language", "required_output_formats", "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "required_output_formats", "is_active" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "generated_output_formats",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "required_output_formats",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_subtitle_format",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "subtitle_output_mode",
                table: "translation_requests");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "target_language", "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);
        }
    }
}
