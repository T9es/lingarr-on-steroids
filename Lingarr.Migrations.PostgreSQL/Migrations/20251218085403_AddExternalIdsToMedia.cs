using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdsToMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "imdb_id",
                table: "shows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tmdb_id",
                table: "shows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tvdb_id",
                table: "shows",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "imdb_id",
                table: "movies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tmdb_id",
                table: "movies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "imdb_id",
                table: "episodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tmdb_id",
                table: "episodes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subtitle_provider_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_id = table.Column<int>(type: "integer", nullable: true),
                    media_type = table.Column<string>(type: "text", nullable: false),
                    provider_name = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subtitle_provider_logs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subtitle_provider_logs");

            migrationBuilder.DropColumn(
                name: "imdb_id",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "tmdb_id",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "tvdb_id",
                table: "shows");

            migrationBuilder.DropColumn(
                name: "imdb_id",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "tmdb_id",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "imdb_id",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "tmdb_id",
                table: "episodes");
        }
    }
}
