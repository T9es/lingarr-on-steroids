using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceSubtitleFreshnessSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "source_snapshot_file_size_bytes",
                table: "translation_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_snapshot_fingerprint",
                table: "translation_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_snapshot_identity",
                table: "translation_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "source_snapshot_last_write_utc",
                table: "translation_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_snapshot_stream_index",
                table: "translation_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_snapshot_type",
                table: "translation_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_snapshot_version",
                table: "translation_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "target_language", "status", "completed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_FreshnessLookup",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_file_size_bytes",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_fingerprint",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_identity",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_last_write_utc",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_stream_index",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_type",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_snapshot_version",
                table: "translation_requests");
        }
    }
}
