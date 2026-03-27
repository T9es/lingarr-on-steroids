using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddTestResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_results",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    subtitle_path = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    poster_path = table.Column<string>(type: "TEXT", nullable: true),
                    source_language = table.Column<string>(type: "TEXT", nullable: false),
                    target_language = table.Column<string>(type: "TEXT", nullable: false),
                    success = table.Column<bool>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    total_lines = table.Column<int>(type: "INTEGER", nullable: false),
                    translated_lines = table.Column<int>(type: "INTEGER", nullable: false),
                    failed_lines = table.Column<int>(type: "INTEGER", nullable: false),
                    duration_seconds = table.Column<double>(type: "REAL", nullable: false),
                    token_usage_prompt = table.Column<int>(type: "INTEGER", nullable: true),
                    token_usage_completion = table.Column<int>(type: "INTEGER", nullable: true),
                    translation_service = table.Column<string>(type: "TEXT", nullable: false),
                    api_calls_json = table.Column<string>(type: "TEXT", nullable: true),
                    line_results_json = table.Column<string>(type: "TEXT", nullable: true),
                    timing_json = table.Column<string>(type: "TEXT", nullable: true),
                    preview_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_results", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_results");
        }
    }
}
