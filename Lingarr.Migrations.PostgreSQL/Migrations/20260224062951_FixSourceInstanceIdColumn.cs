using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class FixSourceInstanceIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Set SourceInstanceId to 'default' for existing records with NULL
            // This is needed for data that was synced before multi-instance support
            migrationBuilder.Sql(
                "UPDATE movies SET source_instance_id = 'default' WHERE source_instance_id IS NULL");
            migrationBuilder.Sql(
                "UPDATE shows SET source_instance_id = 'default' WHERE source_instance_id IS NULL");

            // Step 2: Drop old unique indexes if they exist (from pre-multi-instance)
            // These only allowed one RadarrId/SonarrId globally, preventing multi-instance
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Movies_RadarrId\"");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Shows_SonarrId\"");

            // Step 3: Drop the non-unique composite indexes created by AddMultiInstanceSupport migration
            // We'll replace them with unique versions
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Movies_SourceInstanceId\"");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Shows_SourceInstanceId\"");

            // Step 4: Create unique composite indexes
            // This allows the same RadarrId/SonarrId across different instances,
            // but prevents duplicates within the same instance
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Movies_SourceInstanceId_RadarrId\" " +
                "ON movies (source_instance_id, radarr_id)");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Shows_SourceInstanceId_SonarrId\" " +
                "ON shows (source_instance_id, sonarr_id)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique composite indexes
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Movies_SourceInstanceId_RadarrId\"");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Shows_SourceInstanceId_SonarrId\"");

            // Recreate the non-unique indexes
            migrationBuilder.CreateIndex(
                name: "IX_Movies_SourceInstanceId",
                table: "movies",
                column: "source_instance_id");
            migrationBuilder.CreateIndex(
                name: "IX_Shows_SourceInstanceId",
                table: "shows",
                column: "source_instance_id");

            // Note: We don't recreate the old unique indexes on RadarrId/SonarrId
            // because that would break multi-instance support
        }
    }
}
