using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddApiUsageAndErrorLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_usage_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    service = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    tokens_used = table.Column<int>(type: "INTEGER", nullable: true),
                    response_time_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    success = table.Column<bool>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_usage_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "error_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    stack_trace = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_error_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_logs_service",
                table: "api_usage_logs",
                column: "service");

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_logs_timestamp",
                table: "api_usage_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_error_logs_timestamp",
                table: "error_logs",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_usage_logs");

            migrationBuilder.DropTable(
                name: "error_logs");
        }
    }
}
