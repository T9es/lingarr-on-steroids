using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationRequestIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_translation_requests_completed_at",
                table: "translation_requests",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_translation_requests_status",
                table: "translation_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SourceInstanceId_SonarrId",
                table: "episodes",
                columns: new[] { "source_instance_id", "sonarr_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_translation_requests_completed_at",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "ix_translation_requests_status",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_SourceInstanceId_SonarrId",
                table: "episodes");
        }
    }
}
