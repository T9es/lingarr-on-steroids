using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationRequestActiveDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate active requests before enforcing uniqueness.
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY media_id, media_type, source_language, target_language
               ORDER BY id
           ) rn
    FROM translation_requests
    WHERE status IN (0,1)
)
DELETE FROM translation_requests WHERE id IN (SELECT id FROM ranked WHERE rn > 1);
");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "translation_requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE translation_requests SET is_active = CASE WHEN status IN (0,1) THEN 1 ELSE 0 END;");

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "translation_requests");
        }
    }
}

