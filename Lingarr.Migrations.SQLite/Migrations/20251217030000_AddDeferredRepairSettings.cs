using Microsoft.EntityFrameworkCore.Migrations;

namespace Lingarr.Migrations.SQLite.Migrations
{
    public partial class AddDeferredRepairSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[,]
                {
                    { "batch_retry_mode", "deferred" },
                    { "repair_context_radius", "10" },
                    { "repair_max_retries", "1" },
                    { "chutes_model", "" },
                    { "chutes_api_key", "" },
                    { "chutes_usage_limit_override", "" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValues: new object[]
                {
                    "batch_retry_mode",
                    "repair_context_radius",
                    "repair_max_retries",
                    "chutes_model",
                    "chutes_api_key",
                    "chutes_usage_limit_override"
                });
        }
    }
}
