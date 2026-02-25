using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class FixSourceInstanceIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add retry tracking columns to translation_requests
            migrationBuilder.AddColumn<DateTime>(
                name: "failed_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_retry_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "translation_requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Step 1: Set SourceInstanceId to 'default' for existing records with NULL
            // This is needed for data that was synced before multi-instance support
            // Note: Columns were already added in AddMultiInstanceSupport migration (20260221000000)
            migrationBuilder.Sql(
                "UPDATE movies SET source_instance_id = 'default' WHERE source_instance_id IS NULL");
            migrationBuilder.Sql(
                "UPDATE shows SET source_instance_id = 'default' WHERE source_instance_id IS NULL");

            // Step 2: Drop old unique indexes if they exist (from pre-multi-instance)
            // SQLite doesn't support IF EXISTS for DROP INDEX, so we use a try-catch approach
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Movies_RadarrId");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Shows_SonarrId");

            // Step 3: Create unique composite indexes
            // This allows the same RadarrId/SonarrId across different instances,
            // but prevents duplicates within the same instance
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Movies_SourceInstanceId_RadarrId " +
                "ON movies (source_instance_id, radarr_id)");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Shows_SourceInstanceId_SonarrId " +
                "ON shows (source_instance_id, sonarr_id)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Movies_SourceInstanceId_RadarrId");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_Shows_SourceInstanceId_SonarrId");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "next_retry_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "movies");
        }
    }
}
