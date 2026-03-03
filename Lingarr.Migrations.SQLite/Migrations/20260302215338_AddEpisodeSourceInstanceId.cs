using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
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
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            // Backfill from parent Show for seamless migration
            migrationBuilder.Sql(@"
                UPDATE Episodes
                SET source_instance_id = (
                    SELECT s.source_instance_id
                    FROM Seasons season
                    JOIN Shows s ON s.Id = season.ShowId
                    WHERE season.Id = Episodes.SeasonId
                )
                WHERE source_instance_id IS NULL
            ");

            // Create unique index for efficient querying by instance
            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SourceInstanceId_SonarrId",
                table: "episodes",
                columns: new[] { "source_instance_id", "sonarr_id" },
                unique: true);
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
