using Microsoft.EntityFrameworkCore.Migrations;

namespace Lingarr.Migrations.MySQL.Migrations
{
    public partial class AddSubtitleIntegrityValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO `settings` (`key`, `value`) VALUES
('subtitle_integrity_validation_enabled', 'false')
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValue: "subtitle_integrity_validation_enabled");
        }
    }
}
