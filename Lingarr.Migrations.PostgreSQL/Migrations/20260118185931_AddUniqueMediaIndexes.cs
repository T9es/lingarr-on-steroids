using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMediaIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete duplicate shows keeping the newest (highest Id)
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
