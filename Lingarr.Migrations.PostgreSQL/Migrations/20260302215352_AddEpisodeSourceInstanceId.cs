using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeSourceInstanceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_instance_id",
                table: "episodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Backfill from parent Show for seamless migration
            migrationBuilder.Sql(@"
                UPDATE episodes e
                SET source_instance_id = s.source_instance_id
                FROM seasons season
                JOIN shows s ON s.id = season.show_id
                WHERE season.id = e.season_id
                  AND e.source_instance_id IS NULL
            ");

            // Create index for efficient querying by instance
            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SourceInstanceId_SonarrId",
                table: "episodes",
                columns: new[] { "source_instance_id", "sonarr_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "episodes");
        }
    }
}
