using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationStateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update Movies table
            migrationBuilder.AddColumn<int>(
                name: "translation_state",
                table: "movies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "indexed_at",
                table: "movies",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "state_settings_version",
                table: "movies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Update Episodes table
            migrationBuilder.AddColumn<int>(
                name: "translation_state",
                table: "episodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "indexed_at",
                table: "episodes",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "state_settings_version",
                table: "episodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Create Indexes
            migrationBuilder.CreateIndex(
                name: "IX_Movies_TranslationState",
                table: "movies",
                column: "translation_state");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_TranslationState",
                table: "episodes",
                column: "translation_state");
                
            // Insert default setting
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[] { "translation.language_settings_version", "1" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.DropIndex(
                name: "IX_Movies_TranslationState",
                table: "movies");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_TranslationState",
                table: "episodes");

            // Drop columns from Movies
            migrationBuilder.DropColumn(
                name: "translation_state",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "indexed_at",
                table: "movies");

            migrationBuilder.DropColumn(
                name: "state_settings_version",
                table: "movies");

            // Drop columns from Episodes
            migrationBuilder.DropColumn(
                name: "translation_state",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "indexed_at",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "state_settings_version",
                table: "episodes");
                
            // Delete the setting
            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValue: "translation.language_settings_version");
        }
    }
}
