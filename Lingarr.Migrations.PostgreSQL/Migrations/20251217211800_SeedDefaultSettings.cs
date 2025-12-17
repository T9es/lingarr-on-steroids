using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <summary>
    /// Seeds default settings for existing PostgreSQL databases that were created
    /// before the settings seeding was added to InitialCreate.
    /// Uses INSERT ... ON CONFLICT DO NOTHING to avoid duplicates.
    /// </summary>
    public partial class SeedDefaultSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with ON CONFLICT DO NOTHING to safely insert settings
            // that may or may not already exist
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value) VALUES 
                    ('radarr_api_key', ''),
                    ('radarr_url', ''),
                    ('sonarr_api_key', ''),
                    ('sonarr_url', ''),
                    ('source_languages', '[]'),
                    ('target_languages', '[]'),
                    ('theme', 'lingarr'),
                    ('movie_schedule', '0 4 * * *'),
                    ('show_schedule', '0 4 * * *'),
                    ('radarr_settings_completed', 'false'),
                    ('sonarr_settings_completed', 'false'),
                    ('service_type', ''),
                    ('openai_model', ''),
                    ('openai_api_key', ''),
                    ('anthropic_model', ''),
                    ('anthropic_api_key', ''),
                    ('anthropic_version', ''),
                    ('deepl_api_key', ''),
                    ('libre_translate_url', ''),
                    ('translation_batch_size', '10'),
                    ('local_ai_model', ''),
                    ('local_ai_endpoint', ''),
                    ('local_ai_api_key', ''),
                    ('gemini_model', ''),
                    ('gemini_api_key', ''),
                    ('deepseek_model', ''),
                    ('deepseek_api_key', ''),
                    ('movie_age_threshold', '0'),
                    ('show_age_threshold', '0'),
                    ('fix_overlapping_subtitles', 'false'),
                    ('custom_ai_parameters', '[]'),
                    ('strip_subtitle_formatting', 'false'),
                    ('subtitle_validation_enabled', 'false'),
                    ('subtitle_validation_maxfilesizebytes', '1048576'),
                    ('subtitle_validation_maxsubtitlelength', '500'),
                    ('subtitle_validation_minsubtitlelength', '2'),
                    ('subtitle_validation_mindurationms', '500'),
                    ('subtitle_validation_maxdurationsecs', '10'),
                    ('ai_context_prompt_enabled', 'false'),
                    ('ai_context_prompt', 'Use the CONTEXT to translate the TARGET line.

[TARGET] {lineToTranslate}

[CONTEXT]
{contextBefore}
{lineToTranslate}
{contextAfter}
[/CONTEXT]'),
                    ('ai_context_before', '2'),
                    ('ai_context_after', '2'),
                    ('libre_translate_api_key', ''),
                    ('ignore_captions_in_source_file', 'false'),
                    ('custom_request_limit', ''),
                    ('custom_timeout', ''),
                    ('max_parallel_translations', '1'),
                    ('chutes_request_buffer', '50'),
                    ('enable_batch_fallback', 'true'),
                    ('max_batch_split_attempts', '3'),
                    ('strip_ass_drawing_commands', 'true'),
                    ('clean_source_ass_drawings', 'false'),
                    ('extract_embedded_subtitles', 'false'),
                    ('subtitle_integrity_validation_enabled', 'false'),
                    ('translation.language_settings_version', '1'),
                    ('batch_retry_mode', 'deferred'),
                    ('repair_context_radius', '10'),
                    ('repair_max_retries', '1'),
                    ('chutes_model', ''),
                    ('chutes_api_key', ''),
                    ('chutes_usage_limit_override', ''),
                    ('ai_prompt', 'You are a professional movie, anime and TV subtitle translator working from {sourceLanguage} to {targetLanguage}.

Your job is to produce natural, fluent subtitles in {targetLanguage} that feel like high-quality Netflix subtitles.

Rules:
- Translate the meaning and tone, not word-for-word. Use natural, idiomatic {targetLanguage}, and keep the emotional impact.
- Do NOT censor or soften profanity, insults or slang unless the source already does. Keep roughly the same intensity.
- Preserve the structure of the input: keep the same number of lines and the same line breaks. Do not merge, split or reorder lines.
- Preserve all tags, speaker labels and non-dialogue descriptions such as [MUSIC], [LAUGHTER], (sighs), <i>...</i>, names before a colon, etc.
  Translate only the spoken content; keep formatting and tags as they are.
- Prefer spoken, contemporary {targetLanguage} that sounds like real people talking in a show, not written prose.
- Do NOT transliterate English interjections if they sound unnatural in {targetLanguage}. Use natural equivalents instead (e.g. in Polish ""Hm?"", ""Co?"", ""No nie"", ""O kurde"", etc.).
- For repeated lines and running gags, keep terminology and style consistent across the episode.
- Do not explain or comment on the text. Output ONLY the translated subtitle text, without quotes or extra text.
- Use neutral insults that could appear in Netflix subtitles, not ultra-local or oddly specific ones.')
                ON CONFLICT (key) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Don't delete settings on rollback - they may have user data
        }
    }
}
