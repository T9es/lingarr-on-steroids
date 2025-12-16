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
