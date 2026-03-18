using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_translation_requests_status",
                table: "translation_requests",
                newName: "IX_TranslationRequests_Status");

            migrationBuilder.RenameIndex(
                name: "ix_translation_request_logs_translation_request_id",
                table: "translation_request_logs",
                newName: "IX_TranslationRequestLog_TranslationRequestId");

            migrationBuilder.RenameIndex(
                name: "ix_seasons_show_id",
                table: "seasons",
                newName: "IX_Seasons_ShowId");

            migrationBuilder.RenameIndex(
                name: "ix_embedded_subtitles_movie_id",
                table: "embedded_subtitles",
                newName: "IX_EmbeddedSubtitles_MovieId");

            migrationBuilder.RenameIndex(
                name: "ix_embedded_subtitles_episode_id",
                table: "embedded_subtitles",
                newName: "IX_EmbeddedSubtitles_EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_FailedAt",
                table: "translation_requests",
                column: "failed_at");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_IsPriority",
                table: "translation_requests",
                column: "is_priority");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_NextRetryAt",
                table: "translation_requests",
                column: "next_retry_at");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequests_Status_Priority_Created",
                table: "translation_requests",
                columns: new[] { "status", "is_priority", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_TranslationRequestLog_Level",
                table: "translation_request_logs",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeasonNumber",
                table: "seasons",
                column: "season_number");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddedSubtitles_EpisodeId_IsExtracted",
                table: "embedded_subtitles",
                columns: new[] { "episode_id", "is_extracted" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddedSubtitles_IsExtracted",
                table: "embedded_subtitles",
                column: "is_extracted");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddedSubtitles_Language",
                table: "embedded_subtitles",
                column: "language");

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddedSubtitles_MovieId_IsExtracted",
                table: "embedded_subtitles",
                columns: new[] { "movie_id", "is_extracted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_FailedAt",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_IsPriority",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_NextRetryAt",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "IX_TranslationRequests_Status_Priority_Created",
                table: "translation_requests");

            migrationBuilder.DropIndex(
                name: "IX_TranslationRequestLog_Level",
                table: "translation_request_logs");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_SeasonNumber",
                table: "seasons");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddedSubtitles_EpisodeId_IsExtracted",
                table: "embedded_subtitles");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddedSubtitles_IsExtracted",
                table: "embedded_subtitles");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddedSubtitles_Language",
                table: "embedded_subtitles");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddedSubtitles_MovieId_IsExtracted",
                table: "embedded_subtitles");

            migrationBuilder.RenameIndex(
                name: "IX_TranslationRequests_Status",
                table: "translation_requests",
                newName: "ix_translation_requests_status");

            migrationBuilder.RenameIndex(
                name: "IX_TranslationRequestLog_TranslationRequestId",
                table: "translation_request_logs",
                newName: "ix_translation_request_logs_translation_request_id");

            migrationBuilder.RenameIndex(
                name: "IX_Seasons_ShowId",
                table: "seasons",
                newName: "ix_seasons_show_id");

            migrationBuilder.RenameIndex(
                name: "IX_EmbeddedSubtitles_MovieId",
                table: "embedded_subtitles",
                newName: "ix_embedded_subtitles_movie_id");

            migrationBuilder.RenameIndex(
                name: "IX_EmbeddedSubtitles_EpisodeId",
                table: "embedded_subtitles",
                newName: "ix_embedded_subtitles_episode_id");
        }
    }
}
