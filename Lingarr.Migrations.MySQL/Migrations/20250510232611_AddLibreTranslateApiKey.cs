using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddLibreTranslateApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
INSERT INTO `settings` (`key`, `value`) VALUES
('libretranslate_api_key', '{Environment.GetEnvironmentVariable("LIBRETRANSLATE_API_KEY") ?? ""}')
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
                    "libretranslate_api_key",
                });
        }
    }
}
