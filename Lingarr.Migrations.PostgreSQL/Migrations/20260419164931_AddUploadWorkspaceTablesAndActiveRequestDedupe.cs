using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadWorkspaceTablesAndActiveRequestDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.Sql(
                """
                UPDATE translation_requests
                SET required_output_formats = CASE
                    WHEN lower(coalesce(btrim(subtitle_output_mode), '')) = 'srt-only' THEN '.srt'
                    WHEN lower(coalesce(btrim(source_subtitle_format), '')) IN ('ass', '.ass') AND
                         lower(coalesce(btrim(subtitle_output_mode), '')) = 'both' THEN '.ass,.srt'
                    WHEN lower(coalesce(btrim(source_subtitle_format), '')) IN ('ssa', '.ssa') AND
                         lower(coalesce(btrim(subtitle_output_mode), '')) = 'both' THEN '.srt,.ssa'
                    WHEN source_subtitle_format IS NULL OR btrim(source_subtitle_format) = '' THEN '.srt'
                    WHEN lower(btrim(source_subtitle_format)) IN ('subrip', 'mov_text') THEN '.srt'
                    WHEN lower(btrim(source_subtitle_format)) = 'webvtt' THEN '.vtt'
                    WHEN lower(btrim(source_subtitle_format)) = 'ass' THEN '.ass'
                    WHEN lower(btrim(source_subtitle_format)) = 'ssa' THEN '.ssa'
                    WHEN left(btrim(source_subtitle_format), 1) = '.' THEN lower(btrim(source_subtitle_format))
                    ELSE '.' || lower(btrim(source_subtitle_format))
                END
                WHERE is_active = TRUE
                  AND (required_output_formats IS NULL OR btrim(required_output_formats) = '');
                """);

            migrationBuilder.Sql(
                """
                WITH ranked_active AS (
                    SELECT id,
                       ROW_NUMBER() OVER (
                PARTITION BY workload_item_key, source_language, target_language
                               ORDER BY created_at ASC, id ASC
                           ) AS rn
                    FROM translation_requests
                    WHERE is_active = TRUE
                )
                UPDATE translation_requests tr
                SET is_active = NULL,
                    status = CASE
                        WHEN tr.status IN (0, 1) THEN 5
                        ELSE tr.status
                    END,
                    completed_at = CASE
                        WHEN tr.status IN (0, 1) AND tr.completed_at IS NULL THEN NOW()
                        ELSE tr.completed_at
                    END
                FROM ranked_active ra
                WHERE tr.id = ra.id
                  AND ra.rn > 1;
                """);

            migrationBuilder.CreateTable(
                name: "upload_batches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    default_remux_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_batch_files",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    upload_batch_id = table.Column<int>(type: "integer", nullable: false),
                    file_kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    stored_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    relative_stored_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    detected_source_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    selected_source_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    embed_translated_subtitle = table.Column<bool>(type: "boolean", nullable: false),
                    selected_embedded_stream_index = table.Column<int>(type: "integer", nullable: true),
                    selected_embedded_stream_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    selected_embedded_stream_title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    selected_embedded_stream_codec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    current_translation_request_id = table.Column<int>(type: "integer", nullable: true),
                    probe_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    probe_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batch_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batch_files_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_artifacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    upload_batch_id = table.Column<int>(type: "integer", nullable: false),
                    upload_batch_file_id = table.Column<int>(type: "integer", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_downloadable = table.Column<bool>(type: "boolean", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_artifacts_upload_batch_files_upload_batch_file_id",
                        column: x => x.upload_batch_file_id,
                        principalTable: "upload_batch_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_upload_artifacts_upload_batches_upload_batch_id",
                        column: x => x.upload_batch_id,
                        principalTable: "upload_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_batch_file_subtitle_streams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    upload_batch_file_id = table.Column<int>(type: "integer", nullable: false),
                    stream_index = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    codec_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_text_based = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_forced = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_batch_file_subtitle_streams", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_batch_file_subtitle_streams_upload_batch_files_uploa",
                        column: x => x.upload_batch_file_id,
                        principalTable: "upload_batch_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "source_language", "target_language", "is_active" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadArtifacts_UploadBatchFileId",
                table: "upload_artifacts",
                column: "upload_batch_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_UploadArtifacts_UploadBatchFileId_Kind",
                table: "upload_artifacts",
                columns: new[] { "upload_batch_file_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadArtifacts_UploadBatchId",
                table: "upload_artifacts",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatchFileSubtitleStreams_UploadBatchFileId",
                table: "upload_batch_file_subtitle_streams",
                column: "upload_batch_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatchFileSubtitleStreams_UploadBatchFileId_StreamIndex",
                table: "upload_batch_file_subtitle_streams",
                columns: new[] { "upload_batch_file_id", "stream_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatchFiles_Status",
                table: "upload_batch_files",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatchFiles_UploadBatchId",
                table: "upload_batch_files",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatchFiles_UploadBatchId_OriginalFileName",
                table: "upload_batch_files",
                columns: new[] { "upload_batch_id", "original_file_name" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_ExpiresAt",
                table: "upload_batches",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_Status",
                table: "upload_batches",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_artifacts");

            migrationBuilder.DropTable(
                name: "upload_batch_file_subtitle_streams");

            migrationBuilder.DropTable(
                name: "upload_batch_files");

            migrationBuilder.DropTable(
                name: "upload_batches");

            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "workload_item_key", "source_language", "target_language", "is_active" },
                unique: true);
        }
    }
}
