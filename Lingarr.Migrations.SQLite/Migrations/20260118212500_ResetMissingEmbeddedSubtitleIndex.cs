using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class ResetMissingEmbeddedSubtitleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reset indexed_at for episodes that have indexed_at set but no embedded_subtitles
            // This is a one-time fix for episodes affected by the FK constraint bug
            migrationBuilder.Sql(@"
                UPDATE Episodes 
                SET IndexedAt = NULL 
                WHERE IndexedAt IS NOT NULL 
                  AND Id NOT IN (
                      SELECT DISTINCT EpisodeId 
                      FROM EmbeddedSubtitles 
                      WHERE EpisodeId IS NOT NULL
                  );
            ");
            
            // Same for movies
            migrationBuilder.Sql(@"
                UPDATE Movies 
                SET IndexedAt = NULL 
                WHERE IndexedAt IS NOT NULL 
                  AND Id NOT IN (
                      SELECT DISTINCT MovieId 
                      FROM EmbeddedSubtitles 
                      WHERE MovieId IS NOT NULL
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback needed - the episodes will be re-indexed naturally
        }
    }
}
