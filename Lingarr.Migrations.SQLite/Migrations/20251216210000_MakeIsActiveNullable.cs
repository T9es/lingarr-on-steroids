using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class MakeIsActiveNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the unique index that depends on IsActive
            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            // 2. Update existing data: '0' (inactive) becomes NULL
            migrationBuilder.Sql("UPDATE translation_requests SET is_active = NULL WHERE is_active = 0;");

            // 3. SQLite doesn't support ALTER COLUMN, so we use a workaround:
            // The column is already created, and SQLite is flexible with nullability.
            // For new installations, the EF model will create nullable column.
            // For existing installations, SQLite allows NULL in any column by default.

            // 4. Re-create the unique index
            // SQLite allows multiple NULLs in a unique index
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

            // Identify duplicate inactive requests.
            // A conflict occurs if we have multiple rows that will ALL become 'is_active = 0'.
            // These include rows that are currently 'is_active = 0' AND rows that are 'is_active IS NULL'.
            // We need to group by (media..., effective_is_active=0) and keep one.

            // 1. Re-parent logs from duplicates to the 'keep' record to avoid data loss.
            migrationBuilder.Sql(@"
UPDATE translation_request_logs
SET translation_request_id = (
    SELECT d.keep_id
    FROM translation_requests tr
    JOIN (
        SELECT MIN(id) AS keep_id, media_id, media_type, source_language, target_language
        FROM translation_requests
        WHERE (is_active IS NULL OR is_active = 0)
        GROUP BY media_id, media_type, source_language, target_language
        HAVING COUNT(*) > 1 AND media_id IS NOT NULL
    ) d ON tr.media_id = d.media_id
       AND tr.media_type = d.media_type
       AND tr.source_language = d.source_language
       AND tr.target_language = d.target_language
    WHERE tr.id = translation_request_logs.translation_request_id
      AND (tr.is_active IS NULL OR tr.is_active = 0)
      AND tr.id <> d.keep_id
)
WHERE translation_request_id IN (
    SELECT tr.id
    FROM translation_requests tr
    JOIN (
        SELECT MIN(id) AS keep_id, media_id, media_type, source_language, target_language
        FROM translation_requests
        WHERE (is_active IS NULL OR is_active = 0)
        GROUP BY media_id, media_type, source_language, target_language
        HAVING COUNT(*) > 1 AND media_id IS NOT NULL
    ) d ON tr.media_id = d.media_id
       AND tr.media_type = d.media_type
       AND tr.source_language = d.source_language
       AND tr.target_language = d.target_language
    WHERE (tr.is_active IS NULL OR tr.is_active = 0) AND tr.id <> d.keep_id
);
");

            // 2. Delete the duplicate requests.
            migrationBuilder.Sql(@"
DELETE FROM translation_requests
WHERE id IN (
    SELECT tr.id
    FROM translation_requests tr
    JOIN (
        SELECT MIN(id) AS keep_id, media_id, media_type, source_language, target_language
        FROM translation_requests
        WHERE (is_active IS NULL OR is_active = 0)
        GROUP BY media_id, media_type, source_language, target_language
        HAVING COUNT(*) > 1 AND media_id IS NOT NULL
    ) d ON tr.media_id = d.media_id
       AND tr.media_type = d.media_type
       AND tr.source_language = d.source_language
       AND tr.target_language = d.target_language
    WHERE (tr.is_active IS NULL OR tr.is_active = 0) AND tr.id <> d.keep_id
);
");

            // Set NULLs back to 0
            migrationBuilder.Sql("UPDATE translation_requests SET is_active = 0 WHERE is_active IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);
        }
    }
}
