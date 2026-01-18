using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
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
                UPDATE episodes 
                SET indexed_at = NULL 
                WHERE indexed_at IS NOT NULL 
                  AND id NOT IN (
                      SELECT DISTINCT episode_id 
                      FROM embedded_subtitles 
                      WHERE episode_id IS NOT NULL
                  );
            ");
            
            // Same for movies
            migrationBuilder.Sql(@"
                UPDATE movies 
                SET indexed_at = NULL 
                WHERE indexed_at IS NOT NULL 
                  AND id NOT IN (
                      SELECT DISTINCT movie_id 
                      FROM embedded_subtitles 
                      WHERE movie_id IS NOT NULL
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
