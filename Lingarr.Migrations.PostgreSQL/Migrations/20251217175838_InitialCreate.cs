using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_statistics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    translation_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_statistics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    radarr_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    path = table.Column<string>(type: "text", nullable: true),
                    media_hash = table.Column<string>(type: "text", nullable: true),
                    date_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    translation_age_threshold = table.Column<int>(type: "integer", nullable: true),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false),
                    priority_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    translation_state = table.Column<int>(type: "integer", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    state_settings_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "path_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_path = table.Column<string>(type: "text", nullable: false),
                    destination_path = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_path_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "shows",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sonarr_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    date_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    translation_age_threshold = table.Column<int>(type: "integer", nullable: true),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false),
                    priority_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statistics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    total_lines_translated = table.Column<long>(type: "bigint", nullable: false),
                    total_files_translated = table.Column<long>(type: "bigint", nullable: false),
                    total_characters_translated = table.Column<long>(type: "bigint", nullable: false),
                    total_movies = table.Column<int>(type: "integer", nullable: false),
                    total_episodes = table.Column<int>(type: "integer", nullable: false),
                    total_subtitles = table.Column<int>(type: "integer", nullable: false),
                    translations_by_media_type_json = table.Column<string>(type: "text", nullable: false),
                    translations_by_service_json = table.Column<string>(type: "text", nullable: false),
                    subtitles_by_language_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translation_requests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<string>(type: "text", nullable: true),
                    media_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    source_language = table.Column<string>(type: "text", nullable: false),
                    target_language = table.Column<string>(type: "text", nullable: false),
                    subtitle_to_translate = table.Column<string>(type: "text", nullable: true),
                    translated_subtitle = table.Column<string>(type: "text", nullable: true),
                    media_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    show_id = table.Column<int>(type: "integer", nullable: true),
                    movie_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_images_movies_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_images_shows_show_id",
                        column: x => x.show_id,
                        principalTable: "shows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    path = table.Column<string>(type: "text", nullable: true),
                    show_id = table.Column<int>(type: "integer", nullable: false),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                    table.ForeignKey(
                        name: "fk_seasons_shows_show_id",
                        column: x => x.show_id,
                        principalTable: "shows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_request_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    translation_request_id = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_request_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_request_logs_translation_requests_translation_r",
                        column: x => x.translation_request_id,
                        principalTable: "translation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "episodes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sonarr_id = table.Column<int>(type: "integer", nullable: false),
                    episode_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: true),
                    path = table.Column<string>(type: "text", nullable: true),
                    media_hash = table.Column<string>(type: "text", nullable: true),
                    date_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    season_id = table.Column<int>(type: "integer", nullable: false),
                    exclude_from_translation = table.Column<bool>(type: "boolean", nullable: false),
                    translation_state = table.Column<int>(type: "integer", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    state_settings_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_episodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_episodes_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "embedded_subtitles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stream_index = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    codec_name = table.Column<string>(type: "text", nullable: false),
                    is_text_based = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_forced = table.Column<bool>(type: "boolean", nullable: false),
                    is_extracted = table.Column<bool>(type: "boolean", nullable: false),
                    extracted_path = table.Column<string>(type: "text", nullable: true),
                    episode_id = table.Column<int>(type: "integer", nullable: true),
                    movie_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                });

            migrationBuilder.CreateIndex(
                name: "ix_embedded_subtitles_episode_id",
                table: "embedded_subtitles",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "ix_embedded_subtitles_movie_id",
                table: "embedded_subtitles",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_season_id",
                table: "episodes",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_TranslationState",
                table: "episodes",
                column: "translation_state");

            migrationBuilder.CreateIndex(
                name: "ix_images_movie_id",
                table: "images",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "ix_images_show_id",
                table: "images",
                column: "show_id");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TranslationState",
                table: "movies",
                column: "translation_state");

            migrationBuilder.CreateIndex(
                name: "ix_seasons_show_id",
                table: "seasons",
                column: "show_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_request_logs_translation_request_id",
                table: "translation_request_logs",
                column: "translation_request_id");

            migrationBuilder.CreateIndex(
                name: "ux_translation_requests_active_dedupe",
                table: "translation_requests",
                columns: new[] { "media_id", "media_type", "source_language", "target_language", "is_active" },
                unique: true);

            // Seed default settings (consolidated from all SQLite migrations)
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[,]
                {
                    // From PopulateSettings
                    { "radarr_api_key", "" },
                    { "radarr_url", "" },
                    { "sonarr_api_key", "" },
                    { "sonarr_url", "" },
                    { "source_languages", "[]" },
                    { "target_languages", "[]" },
                    { "theme", "lingarr" },
                    { "movie_schedule", "0 4 * * *" },
                    { "show_schedule", "0 4 * * *" },
                    
                    // From AddCompletedSettings
                    { "radarr_settings_completed", "false" },
                    { "sonarr_settings_completed", "false" },
                    
                    // From UpdateDataSchema
                    { "service_type", "" },
                    { "openai_model", "" },
                    { "openai_api_key", "" },
                    { "anthropic_model", "" },
                    { "anthropic_api_key", "" },
                    { "anthropic_version", "" },
                    { "deepl_api_key", "" },
                    { "libre_translate_url", "" },
                    
                    // From AddSettings
                    { "translation_batch_size", "10" },
                    
                    // From NotificationRework
                    { "local_ai_model", "" },
                    { "local_ai_endpoint", "" },
                    { "local_ai_api_key", "" },
                    
                    // From AddParametersTagsAndSettings
                    { "gemini_model", "" },
                    { "gemini_api_key", "" },
                    { "deepseek_model", "" },
                    { "deepseek_api_key", "" },
                    { "movie_age_threshold", "0" },
                    { "show_age_threshold", "0" },
                    { "fix_overlapping_subtitles", "false" },
                    
                    // From AddLocalAiParameters
                    { "custom_ai_parameters", "[]" },
                    { "strip_subtitle_formatting", "false" },
                    { "subtitle_validation_enabled", "false" },
                    { "subtitle_validation_maxfilesizebytes", "1048576" },
                    { "subtitle_validation_maxsubtitlelength", "500" },
                    { "subtitle_validation_minsubtitlelength", "2" },
                    { "subtitle_validation_mindurationms", "500" },
                    { "subtitle_validation_maxdurationsecs", "10" },
                    { "ai_context_prompt_enabled", "false" },
                    { "ai_context_prompt", "Use the CONTEXT to translate the TARGET line.\n\n[TARGET] {lineToTranslate}\n\n[CONTEXT]\n{contextBefore}\n{lineToTranslate}\n{contextAfter}\n[/CONTEXT]" },
                    { "ai_context_before", "2" },
                    { "ai_context_after", "2" },
                    
                    // From AddLibreTranslateApiKey
                    { "libre_translate_api_key", "" },
                    
                    // From BatchTranslationAndTagging
                    { "ignore_captions_in_source_file", "false" },
                    
                    // From IgnoreCaptionsCustomRateLimitAndTimout
                    { "custom_request_limit", "" },
                    { "custom_timeout", "" },
                    
                    // From ConsolidatedSettingsUpdate
                    { "max_parallel_translations", "1" },
                    { "chutes_request_buffer", "50" },
                    { "enable_batch_fallback", "true" },
                    { "max_batch_split_attempts", "3" },
                    { "strip_ass_drawing_commands", "true" },
                    { "clean_source_ass_drawings", "false" },
                    
                    // From AddEmbeddedSubtitles
                    { "extract_embedded_subtitles", "false" },
                    
                    // From AddSubtitleIntegrityValidation
                    { "subtitle_integrity_validation_enabled", "false" },
                    
                    // From AddTranslationStateTracking
                    { "translation.language_settings_version", "1" },
                    
                    // From AddDeferredRepairSettings
                    { "batch_retry_mode", "deferred" },
                    { "repair_context_radius", "10" },
                    { "repair_max_retries", "1" },
                    { "chutes_model", "" },
                    { "chutes_api_key", "" },
                    { "chutes_usage_limit_override", "" }
                });

            // AI prompt (separate because of the long value)
            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "key", "value" },
                values: new object[] { "ai_prompt", "You are a professional movie, anime and TV subtitle translator working from {sourceLanguage} to {targetLanguage}.\n\nYour job is to produce natural, fluent subtitles in {targetLanguage} that feel like high-quality Netflix subtitles.\n\nRules:\n- Translate the meaning and tone, not word-for-word. Use natural, idiomatic {targetLanguage}, and keep the emotional impact.\n- Do NOT censor or soften profanity, insults or slang unless the source already does. Keep roughly the same intensity.\n- Preserve the structure of the input: keep the same number of lines and the same line breaks. Do not merge, split or reorder lines.\n- Preserve all tags, speaker labels and non-dialogue descriptions such as [MUSIC], [LAUGHTER], (sighs), <i>...</i>, names before a colon, etc.\n  Translate only the spoken content; keep formatting and tags as they are.\n- Prefer spoken, contemporary {targetLanguage} that sounds like real people talking in a show, not written prose.\n- Do NOT transliterate English interjections if they sound unnatural in {targetLanguage}. Use natural equivalents instead (e.g. in Polish \"Hm?\", \"Co?\", \"No nie\", \"O kurde\", etc.).\n- For repeated lines and running gags, keep terminology and style consistent across the episode.\n- Do not explain or comment on the text. Output ONLY the translated subtitle text, without quotes or extra text.\n- Use neutral insults that could appear in Netflix subtitles, not ultra-local or oddly specific ones." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_statistics");

            migrationBuilder.DropTable(
                name: "embedded_subtitles");

            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "path_mappings");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "statistics");

            migrationBuilder.DropTable(
                name: "translation_request_logs");

            migrationBuilder.DropTable(
                name: "episodes");

            migrationBuilder.DropTable(
                name: "movies");

            migrationBuilder.DropTable(
                name: "translation_requests");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "shows");
        }
    }
}
