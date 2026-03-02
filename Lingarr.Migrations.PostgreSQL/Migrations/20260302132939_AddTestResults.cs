using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subtitle_path = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    poster_path = table.Column<string>(type: "text", nullable: true),
                    source_language = table.Column<string>(type: "text", nullable: false),
                    target_language = table.Column<string>(type: "text", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    total_lines = table.Column<int>(type: "integer", nullable: false),
                    translated_lines = table.Column<int>(type: "integer", nullable: false),
                    failed_lines = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                    token_usage_prompt = table.Column<int>(type: "integer", nullable: true),
                    token_usage_completion = table.Column<int>(type: "integer", nullable: true),
                    translation_service = table.Column<string>(type: "text", nullable: false),
                    api_calls_json = table.Column<string>(type: "text", nullable: true),
                    line_results_json = table.Column<string>(type: "text", nullable: true),
                    timing_json = table.Column<string>(type: "text", nullable: true),
                    preview_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
