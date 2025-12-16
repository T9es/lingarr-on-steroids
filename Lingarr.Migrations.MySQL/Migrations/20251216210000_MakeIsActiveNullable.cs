using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
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
            migrationBuilder.Sql("UPDATE `translation_requests` SET `is_active` = NULL WHERE `is_active` = 0;");

            // 3. Alter the column to allow NULLs
            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "translation_requests",
                type: "tinyint(1)",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            // 4. Re-create the unique index
            // MySQL allows multiple NULLs in a unique index, so this puts the uniqueness constraint
            // ONLY on rows where is_active IS NOT NULL (i.e. is_active = 1/true).
            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse operation is tricky because we might have multiple NULLs now,
            // which can't map back to '0' if we enforce uniqueness on '0'.
            // Checks/cleanup would be needed, but for now we attempt best-effort restore.

            migrationBuilder.DropIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests");

            // Set NULLs back to 0
            migrationBuilder.Sql("UPDATE `translation_requests` SET `is_active` = 0 WHERE `is_active` IS NULL;");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "translation_requests",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);
        }
    }
}
