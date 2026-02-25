using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tokens_used = table.Column<int>(type: "integer", nullable: true),
                    response_time_ms = table.Column<long>(type: "bigint", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_usage_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "error_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stack_trace = table.Column<string>(type: "text", nullable: true)
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
