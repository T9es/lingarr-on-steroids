using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomSourcesAndWorkloads : Migration
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

            migrationBuilder.AddColumn<int>(
                name: "custom_media_item_id",
                table: "translation_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "upload_batch_file_id",
                table: "translation_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workload_item_key",
                table: "translation_requests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "workload_kind",
                table: "translation_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE translation_requests
                SET workload_kind = CASE
                        WHEN custom_media_item_id IS NOT NULL THEN 1
                        WHEN upload_batch_file_id IS NOT NULL THEN 2
                        ELSE workload_kind
                    END,
                    workload_item_key = CASE
                        WHEN custom_media_item_id IS NOT NULL THEN 'custom:' || custom_media_item_id::text
                        WHEN upload_batch_file_id IS NOT NULL THEN 'upload:' || upload_batch_file_id::text
                        ELSE 'library:' ||
                            CASE media_type
                                WHEN 0 THEN 'Movie'
                                WHEN 1 THEN 'Show'
                                WHEN 2 THEN 'Season'
                                WHEN 3 THEN 'Episode'
                                ELSE media_type::text
                            END || ':' || COALESCE(media_id, 0)::text
                    END
                WHERE workload_item_key IS NULL OR workload_item_key = '';
                """);

            migrationBuilder.CreateTable(
                name: "custom_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    root_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    recursive = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    include_in_automation = table.Column<bool>(type: "boolean", nullable: false),
                    last_scanned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_scan_result = table.Column<string>(type: "text", nullable: true),
                    last_scan_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_media_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    custom_source_id = table.Column<int>(type: "integer", nullable: false),
                    item_kind = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    relative_path = table.Column<string>(type: "text", nullable: false),
                    media_hash = table.Column<string>(type: "text", nullable: true),
                    date_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    translation_state = table.Column<int>(type: "integer", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    state_settings_version = table.Column<int>(type: "integer", nullable: false),
                    last_subtitle_check_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false),
                    priority_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    series_title = table.Column<string>(type: "text", nullable: true),
                    season_number = table.Column<int>(type: "integer", nullable: true),
                    episode_number = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_media_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_media_items_custom_sources_custom_source_id",
                        column: x => x.custom_source_id,
                        principalTable: "custom_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "target_language", "required_output_formats", "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "source_language", "target_language", "required_output_formats", "is_active" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomMediaItems_CustomSourceId",
                table: "custom_media_items",
                column: "custom_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_CustomMediaItems_CustomSourceId_Path",
                table: "custom_media_items",
                columns: new[] { "custom_source_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomMediaItems_TranslationState",
                table: "custom_media_items",
                column: "translation_state");

            migrationBuilder.CreateIndex(
                name: "IX_CustomSources_Name",
                table: "custom_sources",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomSources_RootPath",
                table: "custom_sources",
                column: "root_path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_media_items");

            migrationBuilder.DropTable(
                name: "custom_sources");

            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "custom_media_item_id",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "upload_batch_file_id",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "workload_item_key",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "workload_kind",
                table: "translation_requests");

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
    }
}
