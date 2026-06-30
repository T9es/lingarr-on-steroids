using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationFailedCueAutoApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "translation_failed_cues",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    translation_request_id = table.Column<int>(type: "INTEGER", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    source_text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    normalized_text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    text_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    auto_approval_eligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    auto_approved_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    approval_sequence_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_failed_cues", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_failed_cues_translation_requests_translation_request_id",
                        column: x => x.translation_request_id,
                        principalTable: "translation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_translation_failed_cues_auto_approval",
                table: "translation_failed_cues",
                columns: new[] { "auto_approval_eligible", "auto_approved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_failed_cues_text_hash",
                table: "translation_failed_cues",
                column: "text_hash");

            migrationBuilder.CreateIndex(
                name: "ux_translation_failed_cues_request_position",
                table: "translation_failed_cues",
                columns: new[] { "translation_request_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_failed_cues");
        }
    }
}
