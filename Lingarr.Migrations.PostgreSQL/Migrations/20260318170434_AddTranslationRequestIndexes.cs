using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
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
        }
    }
}
