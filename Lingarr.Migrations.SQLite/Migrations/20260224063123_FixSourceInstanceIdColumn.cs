using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class FixSourceInstanceIdColumn : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "source_instance_id",
                table: "shows",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_instance_id",
                table: "movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shows_SourceInstanceId_SonarrId",
                table: "shows",
                columns: new[] { "source_instance_id", "sonarr_id" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_SourceInstanceId_RadarrId",
                table: "movies",
                columns: new[] { "source_instance_id", "radarr_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shows_SourceInstanceId_SonarrId",
                table: "shows");

            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceInstanceId_RadarrId",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "next_retry_at",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "translation_requests");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "movies");
        }
    }
}
