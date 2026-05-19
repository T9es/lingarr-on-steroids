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
        
        /// <summary>
        /// JSON array of Radarr instances. Each instance has: id, name, url, apiKey
        /// </summary>
        public const string RadarrInstances = "radarr_instances";
        
        /// <summary>
        /// JSON array of Sonarr instances. Each instance has: id, name, url, apiKey
        /// </summary>
        public const string SonarrInstances = "sonarr_instances";
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

        public static class NanoGpt
        {
            public const string Model = "nanogpt_model";
            public const string ApiKey = "nanogpt_api_key";
            public const string SubscriptionModelsOnly = "nanogpt_subscription_models_only";
            public const string WeeklyTokenAllowance = "nanogpt_weekly_token_allowance";
            public const string TokenReserve = "nanogpt_token_reserve";
            public const string DailyUnitReserve = "nanogpt_daily_unit_reserve";
            public const string MonthlyUnitReserve = "nanogpt_monthly_unit_reserve";
        }

        public static class CrofAi
        {
            public const string Model = "crofai_model";
            public const string ApiKey = "crofai_api_key";
        }

        public static class TokenLimits
        {
            public const string OpenAiTokenLimit = "openai_token_limit";
            public const string AnthropicTokenLimit = "anthropic_token_limit";
            public const string GeminiTokenLimit = "gemini_token_limit";
            public const string DeepSeekTokenLimit = "deepseek_token_limit";
            public const string LocalAiTokenLimit = "localai_token_limit";
            public const string ChutesTokenLimit = "chutes_token_limit";
            public const string NanoGptTokenLimit = "nanogpt_token_limit";
            public const string CrofAiTokenLimit = "crofai_token_limit";
            public const string LocalAiTokenLimitEnabled = "localai_token_limit_enabled";
            public const string ChutesMode = "chutes_mode";
            public const string TokenLimitResetTime = "token_limit_reset_time";
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
        public const string SubtitleTagShort = "subtitle_tag_short";
        public const string CleanupOrphanedSubtitles = "cleanup_orphaned_subtitles";
        public const string IgnoreCaptions = "ignore_captions";
        public const string TranslateSupplementalSubtitles = "translate_supplemental_subtitles";
        public const string RequestTimeout = "request_timeout";
        public const string MaxRetries = "max_retries";
        public const string RetryDelay = "retry_delay";
        public const string RetryDelayMultiplier = "retry_delay_multiplier";
        public const string EnableBatchFallback = "enable_batch_fallback";
        public const string MaxBatchSplitAttempts = "max_batch_split_attempts";
        public const string StripAssDrawingCommands = "strip_ass_drawing_commands";
        public const string CleanSourceAssDrawings = "clean_source_ass_drawings";
        public const string SubtitleOutputMode = "subtitle_output_mode";
        public const string SourceLanguageMode = "source_language_mode";
        
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
        /// Maximum number of request-level retries before a failed translation request
        /// is permanently abandoned. Default: 10. Set to 0 for unlimited retries.
        /// </summary>
        public const string MaxRequestRetries = "max_request_retries";
        
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

        /// <summary>
        /// When enabled, embeds translated subtitles into MKV containers when
        /// the output file path would exceed filesystem limits. Default: true
        /// </summary>
        public const string EmbedInContainer = "embed_in_container";
        /// <summary>
        /// When enabled, quality validation failures block publication. When disabled (default),
        /// failures log a warning instead. Default: false (disabled).
        /// </summary>
        public const string EnablePostTranslationQualityGate = "enable_post_translation_quality_gate";
    }

    public static class Automation
    {
        public const string AutomationEnabled = "automation_enabled";
        public const string TranslationSchedule = "translation_schedule";
        public const string CustomSourceScanSchedule = "custom_source_scan_schedule";
        public const string MaxTranslationsPerRun = "max_translations_per_run";
        public const string TranslationCycle = "translation_cycle";
        public const string MovieSchedule = "movie_schedule";
        public const string ShowSchedule = "show_schedule";
        public const string MovieAgeThreshold = "movie_age_threshold";
        public const string ShowAgeThreshold = "show_age_threshold";
    }

    public static class UploadWorkspace
    {
        public const string StorageRoot = "upload_workspace_storage_root";
        public const string RetentionDays = "upload_workspace_retention_days";
        public const string ReservedWorkerSlots = "upload_workspace_reserved_worker_slots";
        public const string MaxBatchSize = "upload_workspace_max_batch_size";
        public const string MaxFileSizeBytes = "upload_workspace_max_file_size_bytes";
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
        public const string BulkIntegrityAutoQueue = "bulk_integrity_auto_queue";
        public const string BulkIntegrityMaxAutoQueuePerRun = "bulk_integrity_max_auto_queue_per_run";
        
        /// <summary>
        /// When enabled, skip translation if the target language subtitle is already embedded in the media container.
        /// This saves API costs and time when the desired subtitle already exists.
        /// </summary>
        public const string SkipWhenTargetEmbedded = "skip_when_target_embedded";
        
        // Persistent scan results
        public const string LastIntegrityCheckResult = "subtitle_integrity_last_result";
        public const string LastAssVerificationResult = "subtitle_ass_verification_last_result";
        public const string LastQualityAuditResult = "subtitle_quality_audit_last_result";
    }

public static class SubtitleExtraction
    {
        /// <summary>
        /// Extraction mode: "on_demand" | "specific_language" | "extract_all"
        /// </summary>
        public const string ExtractionMode = "subtitle_extraction_mode";
        public const string OcrEnabled = "subtitle_ocr_enabled";
        public const string OcrAutoQueue = "subtitle_ocr_auto_queue";
        public const string OcrMinQualityScore = "subtitle_ocr_min_quality_score";
        public const string OcrLanguages = "subtitle_ocr_languages";
        public const string OcrTranslationPromptEnabled = "subtitle_ocr_translation_prompt_enabled";

        /// <summary>
        /// Enable AI-based language detection for subtitle streams with no language tag.
        /// Default: false (disabled)
        /// </summary>
        public const string DetectUnknownLanguages = "detect_unknown_languages";

        /// <summary>
        /// Cron schedule for the automatic unknown language detection job.
        /// Default: "0 3 * * *" (daily at 3 AM UTC)
        /// </summary>
        public const string DetectUnknownLanguagesSchedule = "detect_unknown_languages_schedule";
    }

    public static class Onboarding
    {
        /// <summary>
        /// Whether the user has completed the onboarding wizard.
        /// </summary>
        public const string Completed = "onboarding_completed";
        
        /// <summary>
        /// Whether the user has explicitly skipped the onboarding wizard.
        /// </summary>
        public const string Skipped = "onboarding_skipped";
        
        /// <summary>
        /// The current step the user is on (for resuming).
        /// </summary>
        public const string CurrentStep = "onboarding_current_step";
        
        /// <summary>
        /// JSON object tracking which steps have been completed.
        /// </summary>
        public const string StepProgress = "onboarding_step_progress";
    }
    
    public static class Dashboard
    {
        /// <summary>
        /// JSON object containing dashboard widget layout configuration.
        /// Includes layout positions, widget visibility, and version.
        /// </summary>
        public const string Layout = "dashboard_layout";
    }
}
