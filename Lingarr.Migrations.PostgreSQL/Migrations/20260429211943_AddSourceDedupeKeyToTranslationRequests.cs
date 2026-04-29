using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceDedupeKeyToTranslationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.AddColumn<string>(
                name: "source_dedupe_key",
                table: "translation_requests",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "primary");

            migrationBuilder.Sql("""
                UPDATE translation_requests
                SET source_dedupe_key = LEFT(
                    'supplemental:' ||
                    CASE
                        WHEN lower(source_subtitle_type) = 'signs/songs' THEN 'signs/songs'
                        WHEN lower(source_subtitle_type) = 'forced' OR is_forced_subtitle = TRUE THEN 'forced'
                        ELSE lower(source_subtitle_type)
                    END ||
                    ':' ||
                    COALESCE(
                        NULLIF(source_snapshot_identity, ''),
                        CASE
                            WHEN source_snapshot_stream_index IS NOT NULL THEN 'stream:' || source_snapshot_stream_index::text
                            ELSE NULL
                        END,
                        NULLIF(subtitle_to_translate, ''),
                        'unknown'),
                    512)
                WHERE lower(source_subtitle_type) IN ('forced', 'signs/songs')
                   OR is_forced_subtitle = TRUE;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "source_language", "target_language", "source_dedupe_key", "is_active" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_dedupe_key",
                table: "translation_requests");

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "source_language", "target_language", "is_active" },
                unique: true);
        }
    }
}
