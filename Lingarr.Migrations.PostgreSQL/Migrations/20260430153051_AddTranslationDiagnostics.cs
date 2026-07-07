using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "translation_diagnostic_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    translation_request_id = table.Column<int>(type: "integer", nullable: true),
                    media_id = table.Column<int>(type: "integer", nullable: true),
                    media_type = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    stage = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    source_path = table.Column<string>(type: "text", nullable: true),
                    target_path = table.Column<string>(type: "text", nullable: true),
                    quarantine_path = table.Column<string>(type: "text", nullable: true),
                    output_format = table.Column<string>(type: "text", nullable: true),
                    source_snapshot_identity = table.Column<string>(type: "text", nullable: true),
                    source_snapshot_fingerprint = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    sample_lines_json = table.Column<string>(type: "text", nullable: false),
                    details_json = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_diagnostic_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationDiagnosticEvents_ExpiresAt",
                table: "translation_diagnostic_events",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationDiagnosticEvents_Media",
                table: "translation_diagnostic_events",
                columns: new[] { "media_type", "media_id" });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationDiagnosticEvents_ReasonCode",
                table: "translation_diagnostic_events",
                column: "reason_code");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationDiagnosticEvents_TranslationRequestId",
                table: "translation_diagnostic_events",
                column: "translation_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_diagnostic_events");
        }
    }
}
