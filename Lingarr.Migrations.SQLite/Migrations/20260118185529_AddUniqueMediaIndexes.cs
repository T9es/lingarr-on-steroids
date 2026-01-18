using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMediaIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, delete embedded_subtitles for episodes belonging to duplicate shows
            migrationBuilder.Sql(@"
                DELETE FROM embedded_subtitles 
                WHERE episode_id IN (
                    SELECT e.id FROM episodes e
                    INNER JOIN seasons s ON e.season_id = s.id
                    INNER JOIN shows sh ON s.show_id = sh.id
                    WHERE sh.id NOT IN (
                        SELECT MAX(id) FROM shows GROUP BY sonarr_id
                    )
                );
            ");

            // Delete embedded_subtitles for duplicate movies
            migrationBuilder.Sql(@"
                DELETE FROM embedded_subtitles 
                WHERE movie_id IN (
                    SELECT id FROM movies WHERE id NOT IN (
                        SELECT MAX(id) FROM movies GROUP BY radarr_id
                    )
                );
            ");

            // Delete duplicate shows keeping the newest (highest Id)
            // Cascade will handle seasons and episodes
            migrationBuilder.Sql(@"
                DELETE FROM shows WHERE id NOT IN (
                    SELECT MAX(id) FROM shows GROUP BY sonarr_id
                );
            ");
            
            // Delete duplicate movies keeping the newest (highest Id)
            migrationBuilder.Sql(@"
                DELETE FROM movies WHERE id NOT IN (
                    SELECT MAX(id) FROM movies GROUP BY radarr_id
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Shows_SonarrId",
                table: "shows",
                column: "sonarr_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movies_RadarrId",
                table: "movies",
                column: "radarr_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shows_SonarrId",
                table: "shows");

            migrationBuilder.DropIndex(
                name: "IX_Movies_RadarrId",
                table: "movies");
        }
    }
}
