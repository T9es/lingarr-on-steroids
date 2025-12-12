using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationRequestActiveDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate active requests before enforcing uniqueness.
            migrationBuilder.Sql(@"
DELETE tr1 FROM translation_requests tr1
JOIN translation_requests tr2
  ON tr1.media_id <=> tr2.media_id
 AND tr1.media_type = tr2.media_type
 AND tr1.source_language = tr2.source_language
 AND tr1.target_language = tr2.target_language
 AND tr1.status IN (0,1)
 AND tr2.status IN (0,1)
 AND tr1.id > tr2.id;
");

            migrationBuilder.Sql(
                "ALTER TABLE `translation_requests` ADD COLUMN IF NOT EXISTS `is_active` tinyint(1) NOT NULL DEFAULT 0;");

            migrationBuilder.Sql(
                "UPDATE translation_requests SET is_active = CASE WHEN status IN (0,1) THEN 1 ELSE 0 END;");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS `ux_translation_requests_active_dedupe` ON `translation_requests` (`media_id`, `media_type`, `source_language`, `target_language`, `is_active`);");
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
