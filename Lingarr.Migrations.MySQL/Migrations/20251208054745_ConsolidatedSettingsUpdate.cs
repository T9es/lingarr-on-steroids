using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.MySQL.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidatedSettingsUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "settings",
                keyColumn: "key",
                keyValue: "ai_prompt",
                column: "value",
                value: "You are a professional movie, anime and TV subtitle translator working from {sourceLanguage} to {targetLanguage}.\n\nYour job is to produce natural, fluent subtitles in {targetLanguage} that feel like high-quality Netflix subtitles.\n\nRules:\n- Translate the meaning and tone, not word-for-word. Use natural, idiomatic {targetLanguage}, and keep the emotional impact.\n- Do NOT censor or soften profanity, insults or slang unless the source already does. Keep roughly the same intensity.\n- Preserve the structure of the input: keep the same number of lines and the same line breaks. Do not merge, split or reorder lines.\n- Preserve all tags, speaker labels and non-dialogue descriptions such as [MUSIC], [LAUGHTER], (sighs), <i>...</i>, names before a colon, etc.\n  Translate only the spoken content; keep formatting and tags as they are.\n- Prefer spoken, contemporary {targetLanguage} that sounds like real people talking in a show, not written prose.\n- Do NOT transliterate English interjections if they sound unnatural in {targetLanguage}. Use natural equivalents instead (e.g. in Polish \"Hm?\", \"Co?\", \"No nie\", \"O kurde\", etc.).\n- For repeated lines and running gags, keep terminology and style consistent across the episode.\n- Do not explain or comment on the text. Output ONLY the translated subtitle text, without quotes or extra text.\n- Use neutral insults that could appear in Netflix subtitles, not ultra-local or oddly specific ones.");

            // Parallel Translation Settings
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[,]
                {
                    { "max_parallel_translations", "1" },
                    { "chutes_request_buffer", "50" }
                });

            // Batch Fallback Settings
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[,]
                {
                    { "enable_batch_fallback", "true" },
                    { "max_batch_split_attempts", "3" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "settings",
                keyColumn: "key",
                keyValue: "ai_prompt",
                column: "value",
                value: "Translate from {sourceLanguage} to {targetLanguage}, preserving the tone and meaning without censoring the content. Adjust punctuation as needed to make the translation sound natural. Provide only the translated text as output, with no additional comments.");

            migrationBuilder.DeleteData(
                table: "settings",
                keyColumn: "key",
                keyValues: new object[]
                {
                    "max_parallel_translations",
                    "chutes_request_buffer",
                    "enable_batch_fallback",
                    "max_batch_split_attempts"
                });
        }
    }
}
