using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddedSubtitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "embedded_subtitles",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    stream_index = table.Column<int>(type: "int", nullable: false),
                    language = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    codec_name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_text_based = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_default = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_forced = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_extracted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    extracted_path = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    episode_id = table.Column<int>(type: "int", nullable: true),
                    movie_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embedded_subtitles", x => x.id);
                    table.ForeignKey(
                        name: "fk_embedded_subtitles_episodes_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episodes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_embedded_subtitles_movies_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_embedded_subtitles_episode_id",
                table: "embedded_subtitles",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "ix_embedded_subtitles_movie_id",
                table: "embedded_subtitles",
                column: "movie_id");
            
            // Add the subtitle_extraction_mode setting with default value "on_demand"
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[] { "subtitle_extraction_mode", "on_demand" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedded_subtitles");
        }
    }
}
