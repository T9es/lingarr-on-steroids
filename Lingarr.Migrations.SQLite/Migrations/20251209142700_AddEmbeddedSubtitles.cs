using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
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
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    stream_index = table.Column<int>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    codec_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_text_based = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_forced = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_extracted = table.Column<bool>(type: "INTEGER", nullable: false),
                    extracted_path = table.Column<string>(type: "TEXT", nullable: true),
                    episode_id = table.Column<int>(type: "INTEGER", nullable: true),
                    movie_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedded_subtitles", x => x.id);
                    table.ForeignKey(
                        name: "FK_embedded_subtitles_episodes_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_embedded_subtitles_movies_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_embedded_subtitles_episode_id",
                table: "embedded_subtitles",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "IX_embedded_subtitles_movie_id",
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
            
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValue: "subtitle_extraction_mode");
        }
    }
}
