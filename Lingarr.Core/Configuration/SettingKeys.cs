namespace Lingarr.Core.Configuration;

public static class SettingKeys
{
    public static class Integration
    {
        public const string RadarrUrl = "radarr_url";
        public const string RadarrApiKey = "radarr_api_key";
        public const string SonarrUrl = "sonarr_url";
        public const string SonarrApiKey = "sonarr_api_key";
        public const string RadarrSettingsCompleted = "radarr_settings_completed";
        public const string SonarrSettingsCompleted = "sonarr_settings_completed";
    }

    public static class Translation
    {
        public const string ServiceType = "service_type";
        public const string MaxParallelTranslations = "max_parallel_translations";

        public static class OpenAi
        {
            public const string Model = "openai_model";
            public const string ApiKey = "openai_api_key";
        }

        public static class Anthropic
        {
            public const string Model = "anthropic_model";
            public const string ApiKey = "anthropic_api_key";
            public const string Version = "anthropic_version";
        }

        public static class LocalAi
        {
            public const string Model = "local_ai_model";
            public const string Endpoint = "local_ai_endpoint";
            public const string ApiKey = "local_ai_api_key";
        }

        public static class DeepL
        {
            public const string DeeplApiKey = "deepl_api_key";
        }

        public static class Gemini
        {
            public const string Model = "gemini_model";
            public const string ApiKey = "gemini_api_key";
        }

        public static class DeepSeek
        {
            public const string Model = "deepseek_model";
            public const string ApiKey = "deepseek_api_key";
        }

        public static class Chutes
        {
            public const string Model = "chutes_model";
            public const string ApiKey = "chutes_api_key";
            public const string UsageLimitOverride = "chutes_usage_limit_override";
            public const string RequestBuffer = "chutes_request_buffer";
        }

        public static class LibreTranslate
        {
            public const string Url = "libretranslate_url";
            public const string ApiKey = "libretranslate_api_key";
        }

        public const string SourceLanguages = "source_languages";
        public const string TargetLanguages = "target_languages";
        public const string AiPrompt = "ai_prompt";
        public const string CustomAiParameters = "custom_ai_parameters";
        public const string AiContextPromptEnabled = "ai_context_prompt_enabled";
        public const string AiContextPrompt = "ai_context_prompt";
        public const string AiContextBefore = "ai_context_before";
        public const string AiContextAfter = "ai_context_after";
        public const string FixOverlappingSubtitles = "fix_overlapping_subtitles";
        public const string StripSubtitleFormatting = "strip_subtitle_formatting";
        public const string AddTranslatorInfo = "add_translator_info";
        public const string UseBatchTranslation = "use_batch_translation";
        public const string MaxBatchSize = "max_batch_size";
        public const string UseSubtitleTagging = "use_subtitle_tagging";
        public const string RemoveLanguageTag = "remove_language_tag";
        public const string SubtitleTag = "subtitle_tag";
        public const string IgnoreCaptions = "ignore_captions";
        public const string RequestTimeout = "request_timeout";
        public const string MaxRetries = "max_retries";
        public const string RetryDelay = "retry_delay";
        public const string RetryDelayMultiplier = "retry_delay_multiplier";
        public const string EnableBatchFallback = "enable_batch_fallback";
        public const string MaxBatchSplitAttempts = "max_batch_split_attempts";
        public const string StripAssDrawingCommands = "strip_ass_drawing_commands";
        public const string CleanSourceAssDrawings = "clean_source_ass_drawings";
        
        /// <summary>
        /// Batch retry mode: "immediate" (split and retry now) or "deferred" (collect failures, repair at end)
        /// </summary>
        public const string BatchRetryMode = "batch_retry_mode";
        
        /// <summary>
        /// Number of surrounding lines to include as context when repairing failed translations
        /// </summary>
        public const string RepairContextRadius = "repair_context_radius";
        
        /// <summary>
        /// Maximum number of retries for the repair batch (default: 1)
        /// </summary>
        public const string RepairMaxRetries = "repair_max_retries";
        
        /// <summary>
        /// Integer version that increments when source/target languages or ignore_captions change.
        /// Media with StateSettingsVersion != this value need re-analysis.
        /// </summary>
        public const string LanguageSettingsVersion = "language_settings_version";
        
        /// <summary>
        /// Enable wrapper context for batch translations (pre/post context around entire batch)
        /// </summary>
        public const string BatchContextEnabled = "batch_context_enabled";
        
        /// <summary>
        /// Number of context lines to include before the first item in each batch
        /// </summary>
        public const string BatchContextBefore = "batch_context_before";
        
        /// <summary>
        /// Number of context lines to include after the last item in each batch
        /// </summary>
        public const string BatchContextAfter = "batch_context_after";
    }

    public static class Automation
    {
        public const string AutomationEnabled = "automation_enabled";
        public const string TranslationSchedule = "translation_schedule";
        public const string MaxTranslationsPerRun = "max_translations_per_run";
        public const string TranslationCycle = "translation_cycle";
        public const string MovieSchedule = "movie_schedule";
        public const string ShowSchedule = "show_schedule";
        public const string MovieAgeThreshold = "movie_age_threshold";
        public const string ShowAgeThreshold = "show_age_threshold";
    }

    public static class SubtitleValidation
    {
        public const string MaxFileSizeBytes = "subtitle_validation_maxfilesizebytes";
        public const string MaxSubtitleLength = "subtitle_validation_maxsubtitlelength";
        public const string MinSubtitleLength = "subtitle_validation_minsubtitlelength";
        public const string MinDurationMs = "subtitle_validation_mindurationms";
        public const string MaxDurationSecs = "subtitle_validation_maxdurationsecs";
        public const string ValidateSubtitles = "subtitle_validation_enabled";
        public const string IntegrityValidationEnabled = "subtitle_integrity_validation_enabled";
    }

    public static class SubtitleExtraction
    {
        /// <summary>
        /// Extraction mode: "on_demand" | "specific_language" | "extract_all"
        /// </summary>
        public const string ExtractionMode = "subtitle_extraction_mode";
    }
}
