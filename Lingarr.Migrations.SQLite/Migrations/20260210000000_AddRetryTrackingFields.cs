using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "failed_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_retry_at",
                table: "translation_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "translation_requests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "next_retry_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "translation_requests");
        }
    }
}
