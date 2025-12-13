using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO `settings` (`key`, `value`) VALUES
('radarr_settings_completed', 'false'),
('sonarr_settings_completed', 'false')
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValues: new object[] { "radarr_settings_completed" });
            
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValues: new object[] { "sonarr_settings_completed" });
        }
    }
}
