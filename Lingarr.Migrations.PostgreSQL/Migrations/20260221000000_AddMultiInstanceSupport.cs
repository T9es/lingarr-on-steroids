using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lingarr.Core.Data;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LingarrDbContext))]
    [Migration("20260221000000_AddMultiInstanceSupport")]
    public partial class AddMultiInstanceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add SourceInstanceId to Movies table for multi-Radarr instance support
            migrationBuilder.AddColumn<string>(
                name: "source_instance_id",
                table: "movies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Add index for efficient querying by instance
            migrationBuilder.CreateIndex(
                name: "IX_Movies_SourceInstanceId",
                table: "movies",
                column: "source_instance_id");

            // Add SourceInstanceId to Shows table for multi-Sonarr instance support
            migrationBuilder.AddColumn<string>(
                name: "source_instance_id",
                table: "shows",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Add index for efficient querying by instance
            migrationBuilder.CreateIndex(
                name: "IX_Shows_SourceInstanceId",
                table: "shows",
                column: "source_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceInstanceId",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "movies");

            migrationBuilder.DropIndex(
                name: "IX_Shows_SourceInstanceId",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "source_instance_id",
                table: "shows");
        }
    }
}
