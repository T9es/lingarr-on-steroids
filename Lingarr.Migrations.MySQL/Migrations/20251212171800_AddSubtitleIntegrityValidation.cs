using Microsoft.EntityFrameworkCore.Migrations;

namespace Lingarr.Migrations.MySQL.Migrations
{
    public partial class AddSubtitleIntegrityValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[] { "subtitle_integrity_validation_enabled", "false" });
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
