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
            // Update the ai_prompt value (simplified version without newlines for SQL)
            migrationBuilder.Sql(@"
UPDATE `settings` SET `value` = 'You are a professional movie, anime and TV subtitle translator working from {sourceLanguage} to {targetLanguage}. Your job is to produce natural, fluent subtitles that feel like high-quality Netflix subtitles. Rules: Translate the meaning and tone, not word-for-word. Use natural, idiomatic language and keep the emotional impact. Do NOT censor or soften profanity, insults or slang unless the source already does. Preserve the structure of the input: keep the same number of lines and line breaks. Preserve all tags, speaker labels and non-dialogue descriptions. Prefer spoken, contemporary language that sounds like real people talking in a show. Do NOT transliterate English interjections if they sound unnatural. For repeated lines, keep terminology consistent. Do not explain or comment on the text. Output ONLY the translated subtitle text, without quotes or extra text.'
WHERE `key` = 'ai_prompt';

INSERT INTO `settings` (`key`, `value`) VALUES
('max_parallel_translations', '1'),
('chutes_request_buffer', '50'),
('enable_batch_fallback', 'true'),
('max_batch_split_attempts', '3'),
('strip_ass_drawing_commands', 'true'),
('clean_source_ass_drawings', 'false')
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE `settings` SET `value` = 'Translate from {sourceLanguage} to {targetLanguage}, preserving the tone and meaning without censoring the content. Adjust punctuation as needed to make the translation sound natural. Provide only the translated text as output, with no additional comments.'
WHERE `key` = 'ai_prompt';

DELETE FROM `settings` WHERE `key` IN (
    'max_parallel_translations',
    'chutes_request_buffer',
    'enable_batch_fallback',
    'max_batch_split_attempts',
    'strip_ass_drawing_commands',
    'clean_source_ass_drawings'
);
");
        }
    }
}
