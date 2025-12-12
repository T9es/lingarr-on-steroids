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
            migrationBuilder.Sql(
                "ALTER TABLE `translation_requests` ADD COLUMN IF NOT EXISTS `is_active` tinyint(1) NOT NULL DEFAULT 0;");

            migrationBuilder.Sql(
                "UPDATE translation_requests SET is_active = CASE WHEN status IN (0,1) THEN 1 ELSE 0 END;");

            // Ensure only one active request exists per (media_id, media_type, source_language, target_language)
            // without deleting rows (some installations may have restrictive FK constraints).
            migrationBuilder.Sql(@"
UPDATE translation_requests tr
JOIN (
    SELECT MIN(id) AS keep_id, media_id, media_type, source_language, target_language
    FROM translation_requests
    WHERE status IN (0,1)
    GROUP BY media_id, media_type, source_language, target_language
    HAVING COUNT(*) > 1
) d
  ON tr.media_id <=> d.media_id
 AND tr.media_type = d.media_type
 AND tr.source_language = d.source_language
 AND tr.target_language = d.target_language
SET tr.is_active = 0
WHERE tr.status IN (0,1) AND tr.id <> d.keep_id;
");

            // Prefer INPLACE to avoid table rebuilds that can fail with foreign key constraints.
            migrationBuilder.Sql(
                "ALTER TABLE `translation_requests` ADD UNIQUE INDEX `ux_translation_requests_active_dedupe` (`media_id`, `media_type`, `source_language`, `target_language`, `is_active`), ALGORITHM=INPLACE, LOCK=NONE;");
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
