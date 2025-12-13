using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO `settings` (`key`, `value`) VALUES
('openai_model', ''),
('openai_api_key', ''),
('anthropic_api_key', ''),
('anthropic_model', ''),
('anthropic_version', '')
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValues: new object[]
                {
                    "openai_model",
                    "openai_api_key",
                    "anthropic_api_key",
                    "anthropic_model",
                    "anthropic_version"
                });
        }
    }
}
