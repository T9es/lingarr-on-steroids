using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class FixEmbeddedSubtitlesColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extracted_path",
                table: "embedded_subtitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_extracted",
                table: "embedded_subtitles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_text_based",
                table: "embedded_subtitles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "codec_name",
                table: "embedded_subtitles",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extracted_path",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "is_extracted",
                table: "embedded_subtitles");

            migrationBuilder.DropColumn(
                name: "is_text_based",
                table: "embedded_subtitles");

            migrationBuilder.AlterColumn<string>(
                name: "codec_name",
                table: "embedded_subtitles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
