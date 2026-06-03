import { ILanguage } from '@/ts/language'
import { ILocale, ITheme } from '@/ts/store'

export interface IInstance {
    id: string
    name: string
    url: string
    apiKey: string
}

export const SETTINGS = {
    RADARR_API_KEY: 'radarr_api_key',
    RADARR_URL: 'radarr_url',
    SONARR_API_KEY: 'sonarr_api_key',
    SONARR_URL: 'sonarr_url',
    SOURCE_LANGUAGES: 'source_languages',
    TARGET_LANGUAGES: 'target_languages',
    SOURCE_LANGUAGE_MODE: 'source_language_mode',
    SONARR_SETTINGS_COMPLETED: 'sonarr_settings_completed',
    RADARR_SETTINGS_COMPLETED: 'radarr_settings_completed',
    SERVICE_TYPE: 'service_type',
    LIBRETRANSLATE_URL: 'libretranslate_url',
    LIBRETRANSLATE_API_KEY: 'libretranslate_api_key',
    DEEPL_API_KEY: 'deepl_api_key',
    SHOW_SCHEDULE: 'show_schedule',
    MOVIE_SCHEDULE: 'movie_schedule',
    MAX_TRANSLATIONS_PER_RUN: 'max_translations_per_run',
    AUTOMATION_ENABLED: 'automation_enabled',
    TRANSLATION_SCHEDULE: 'translation_schedule',
    OPENAI_MODEL: 'openai_model',
    OPENAI_API_KEY: 'openai_api_key',
    ANTHROPIC_MODEL: 'anthropic_model',
    ANTHROPIC_API_KEY: 'anthropic_api_key',
    ANTHROPIC_VERSION: 'anthropic_version',
    LOCAL_AI_ENDPOINT: 'local_ai_endpoint',
    LOCAL_AI_MODEL: 'local_ai_model',
    LOCAL_AI_API_KEY: 'local_ai_api_key',
    GEMINI_MODEL: 'gemini_model',
    GEMINI_API_KEY: 'gemini_api_key',
    DEEPSEEK_MODEL: 'deepseek_model',
    DEEPSEEK_API_KEY: 'deepseek_api_key',
    CHUTES_MODEL: 'chutes_model',
    CHUTES_API_KEY: 'chutes_api_key',
    CHUTES_USAGE_LIMIT_OVERRIDE: 'chutes_usage_limit_override',
    NANOGPT_MODEL: 'nanogpt_model',
    NANOGPT_API_KEY: 'nanogpt_api_key',
    CROFAI_MODEL: 'crofai_model',
    CROFAI_API_KEY: 'crofai_api_key',
    NANOGPT_SUBSCRIPTION_MODELS_ONLY: 'nanogpt_subscription_models_only',
    NANOGPT_WEEKLY_TOKEN_ALLOWANCE: 'nanogpt_weekly_token_allowance',
    NANOGPT_TOKEN_RESERVE: 'nanogpt_token_reserve',
    NANOGPT_DAILY_UNIT_RESERVE: 'nanogpt_daily_unit_reserve',
    NANOGPT_MONTHLY_UNIT_RESERVE: 'nanogpt_monthly_unit_reserve',
    AI_PROMPT: 'ai_prompt',
    THEME: 'theme',
    LOCALE: 'locale',
    MOVIE_AGE_THRESHOLD: 'movie_age_threshold',
    SHOW_AGE_THRESHOLD: 'show_age_threshold',
    FIX_OVERLAPPING_SUBTITLES: 'fix_overlapping_subtitles',
    SUBTITLE_OUTPUT_MODE: 'subtitle_output_mode',
    STRIP_SUBTITLE_FORMATTING: 'strip_subtitle_formatting',
    ADD_TRANSLATOR_INFO: 'add_translator_info',
    CUSTOM_AI_PARAMETERS: 'custom_ai_parameters',
    SUBTITLE_VALIDATION_ENABLED: 'subtitle_validation_enabled',
    SUBTITLE_VALIDATION_MAXDURATIONSECS: 'subtitle_validation_maxdurationsecs',
    SUBTITLE_VALIDATION_MINDURATIONMS: 'subtitle_validation_mindurationms',
    SUBTITLE_VALIDATION_MINSUBTITLELENGTH: 'subtitle_validation_minsubtitlelength',
    SUBTITLE_VALIDATION_MAXSUBTITLELENGTH: 'subtitle_validation_maxsubtitlelength',
    SUBTITLE_VALIDATION_MAXFILESIZEBYTES: 'subtitle_validation_maxfilesizebytes',
    SUBTITLE_INTEGRITY_VALIDATION_ENABLED: 'subtitle_integrity_validation_enabled',
    BULK_INTEGRITY_AUTO_QUEUE: 'bulk_integrity_auto_queue',
    BULK_INTEGRITY_MAX_AUTO_QUEUE_PER_RUN: 'bulk_integrity_max_auto_queue_per_run',
    AI_CONTEXT_PROMPT_ENABLED: 'ai_context_prompt_enabled',
    AI_CONTEXT_PROMPT: 'ai_context_prompt',
    AI_CONTEXT_BEFORE: 'ai_context_before',
    AI_CONTEXT_AFTER: 'ai_context_after',
    USE_BATCH_TRANSLATION: 'use_batch_translation',
    MAX_BATCH_SIZE: 'max_batch_size',
    USE_SUBTITLE_TAGGING: 'use_subtitle_tagging',
    REMOVE_LANGUAGE_TAG: 'remove_language_tag',
    SUBTITLE_TAG: 'subtitle_tag',
    SUBTITLE_TAG_SHORT: 'subtitle_tag_short',
    CLEANUP_ORPHANED_SUBTITLES: 'cleanup_orphaned_subtitles',
    IGNORE_CAPTIONS: 'ignore_captions',
    TRANSLATE_SUPPLEMENTAL_SUBTITLES: 'translate_supplemental_subtitles',
    MAX_RETRIES: 'max_retries',
    RETRY_DELAY: 'retry_delay',
    RETRY_DELAY_MULTIPLIER: 'retry_delay_multiplier',
    MAX_PARALLEL_TRANSLATIONS: 'max_parallel_translations',
    CHUTES_REQUEST_BUFFER: 'chutes_request_buffer',
    ENABLE_BATCH_FALLBACK: 'enable_batch_fallback',
    MAX_BATCH_SPLIT_ATTEMPTS: 'max_batch_split_attempts',
    BATCH_RETRY_MODE: 'batch_retry_mode',
    REPAIR_CONTEXT_RADIUS: 'repair_context_radius',
    REPAIR_MAX_RETRIES: 'repair_max_retries',
    STRIP_ASS_DRAWING_COMMANDS: 'strip_ass_drawing_commands',
    CLEAN_SOURCE_ASS_DRAWINGS: 'clean_source_ass_drawings',
    BATCH_CONTEXT_ENABLED: 'batch_context_enabled',
    BATCH_CONTEXT_BEFORE: 'batch_context_before',
    BATCH_CONTEXT_AFTER: 'batch_context_after',
    ENABLE_POST_TRANSLATION_QUALITY_GATE: 'enable_post_translation_quality_gate',
    SKIP_WHEN_TARGET_EMBEDDED: 'skip_when_target_embedded',
    SUBTITLE_OCR_ENABLED: 'subtitle_ocr_enabled',
    SUBTITLE_OCR_AUTO_QUEUE: 'subtitle_ocr_auto_queue',
    SUBTITLE_OCR_MIN_QUALITY_SCORE: 'subtitle_ocr_min_quality_score',
    SUBTITLE_OCR_LANGUAGES: 'subtitle_ocr_languages',
    SUBTITLE_OCR_TRANSLATION_PROMPT_ENABLED: 'subtitle_ocr_translation_prompt_enabled',
    EMBED_IN_CONTAINER: 'embed_in_container',
    EMBED_WHEN_PATH_TOO_LONG: 'embed_when_path_too_long',
    DETECT_UNKNOWN_LANGUAGES: 'detect_unknown_languages',
    DETECT_UNKNOWN_LANGUAGES_SCHEDULE: 'detect_unknown_languages_schedule',
    MAX_REQUEST_RETRIES: 'max_request_retries',
    RADARR_INSTANCES: 'radarr_instances',
    SONARR_INSTANCES: 'sonarr_instances',
    ONBOARDING_COMPLETED: 'onboarding_completed',
    ONBOARDING_SKIPPED: 'onboarding_skipped',
    ONBOARDING_CURRENT_STEP: 'onboarding_current_step',
    ONBOARDING_STEP_PROGRESS: 'onboarding_step_progress',

    // Token limits
    OPENAI_TOKEN_LIMIT: 'openai_token_limit',
    ANTHROPIC_TOKEN_LIMIT: 'anthropic_token_limit',
    GEMINI_TOKEN_LIMIT: 'gemini_token_limit',
    DEEPSEEK_TOKEN_LIMIT: 'deepseek_token_limit',
    LOCALAI_TOKEN_LIMIT: 'localai_token_limit',
    LOCALAI_TOKEN_LIMIT_ENABLED: 'localai_token_limit_enabled',
    CHUTES_TOKEN_LIMIT: 'chutes_token_limit',
    NANOGPT_TOKEN_LIMIT: 'nanogpt_token_limit',
    CROFAI_TOKEN_LIMIT: 'crofai_token_limit',
    CHUTES_MODE: 'chutes_mode',
    TOKEN_LIMIT_RESET_TIME: 'token_limit_reset_time'
} as const

export const ENCRYPTED_SETTING_KEYS = new Set<string>([
    SETTINGS.RADARR_API_KEY,
    SETTINGS.SONARR_API_KEY,
    SETTINGS.LIBRETRANSLATE_API_KEY,
    SETTINGS.DEEPL_API_KEY,
    SETTINGS.OPENAI_API_KEY,
    SETTINGS.ANTHROPIC_API_KEY,
    SETTINGS.LOCAL_AI_API_KEY,
    SETTINGS.GEMINI_API_KEY,
    SETTINGS.DEEPSEEK_API_KEY,
    SETTINGS.CHUTES_API_KEY,
    SETTINGS.NANOGPT_API_KEY,
    SETTINGS.CROFAI_API_KEY,
    SETTINGS.RADARR_INSTANCES,
    SETTINGS.SONARR_INSTANCES
])

export const isEncryptedSettingKey = (key: string): boolean => {
    return ENCRYPTED_SETTING_KEYS.has(key)
}

export interface ISettings {
    radarr_api_key: string
    radarr_url: string
    sonarr_api_key: string
    sonarr_url: string
    service_type: string
    libretranslate_url: string
    libretranslate_api_key: string
    deepl_api_key: string
    show_schedule: string
    movie_schedule: string
    max_translations_per_run: string
    translation_schedule: string
    source_languages: string | ILanguage[]
    target_languages: string | ILanguage[]
    source_language_mode: string
    automation_enabled: string
    sonarr_settings_completed: string
    radarr_settings_completed: string
    openai_model: string
    openai_api_key: string
    anthropic_model: string
    anthropic_api_key: string
    anthropic_version: string
    local_ai_endpoint: string
    local_ai_model: string
    local_ai_api_key: string
    gemini_model: string
    gemini_api_key: string
    deepseek_model: string
    deepseek_api_key: string
    chutes_model: string
    chutes_api_key: string
    chutes_usage_limit_override: string
    nanogpt_model: string
    nanogpt_api_key: string
    nanogpt_subscription_models_only: string
    nanogpt_weekly_token_allowance: string
    nanogpt_token_reserve: string
    nanogpt_daily_unit_reserve: string
    nanogpt_monthly_unit_reserve: string
    crofai_model: string
    crofai_api_key: string
    ai_prompt: string
    movie_age_threshold: string
    show_age_threshold: string
    fix_overlapping_subtitles: string
    subtitle_output_mode: string
    strip_subtitle_formatting: string
    add_translator_info: string
    theme: ITheme
    locale: ILocale
    custom_ai_parameters: string | ICustomAiParams[]
    subtitle_validation_enabled: string
    subtitle_validation_maxfilesizebytes: string
    subtitle_validation_minsubtitlelength: string
    subtitle_validation_maxsubtitlelength: string
    subtitle_validation_mindurationms: string
    subtitle_validation_maxdurationsecs: string
    subtitle_integrity_validation_enabled: string
    bulk_integrity_auto_queue: string
    bulk_integrity_max_auto_queue_per_run: string
    ai_context_prompt_enabled: string
    ai_context_prompt: string
    ai_context_before: string
    ai_context_after: string
    use_batch_translation: string
    max_batch_size: string
    use_subtitle_tagging: string
    remove_language_tag: string
    subtitle_tag: string
    subtitle_tag_short: string
    cleanup_orphaned_subtitles: string
    ignore_captions: string
    translate_supplemental_subtitles: string
    max_retries: string
    retry_delay: string
    retry_delay_multiplier: string
    max_parallel_translations: string
    chutes_request_buffer: string
    enable_batch_fallback: string
    max_batch_split_attempts: string
    batch_retry_mode: string
    repair_context_radius: string
    repair_max_retries: string
    strip_ass_drawing_commands: string
    clean_source_ass_drawings: string
    batch_context_enabled: string
    batch_context_before: string
    batch_context_after: string
    enable_post_translation_quality_gate: string
    skip_when_target_embedded: string
    subtitle_ocr_enabled: string
    subtitle_ocr_auto_queue: string
    subtitle_ocr_min_quality_score: string
    subtitle_ocr_languages: string
    subtitle_ocr_translation_prompt_enabled: string
    embed_in_container: string
    embed_when_path_too_long: string
    detect_unknown_languages: string
    detect_unknown_languages_schedule: string
    max_request_retries: string
    radarr_instances: string | IInstance[]
    sonarr_instances: string | IInstance[]
    onboarding_completed: string
    onboarding_skipped: string
    onboarding_current_step: string
    onboarding_step_progress: string
    // Token limits
    openai_token_limit: string
    anthropic_token_limit: string
    gemini_token_limit: string
    deepseek_token_limit: string
    localai_token_limit: string
    localai_token_limit_enabled: string
    chutes_token_limit: string
    nanogpt_token_limit: string
    crofai_token_limit: string
    chutes_mode: string
    token_limit_reset_time: string
}

export interface ICustomAiParams {
    key: string
    value: string
}

export const SERVICE_TYPE = {
    LIBRETRANSLATE: 'libretranslate',
    OPENAI: 'openai',
    ANTHROPIC: 'anthropic',
    LOCALAI: 'localai',
    DEEPL: 'deepl',
    GEMINI: 'gemini',
    DEEPSEEK: 'deepseek',
    GOOGLE: 'google',
    BING: 'bing',
    MICROSOFT: 'microsoft',
    YANDEX: 'yandex',
    CHUTES: 'chutes',
    NANOGPT: 'nanogpt',
    CROFAI: 'crofai'
} as const

export type ServiceType = (typeof SERVICE_TYPE)[keyof typeof SERVICE_TYPE]

export interface IFilterOptions {
    logLevel: string
}

export interface ILogEntry {
    logLevel: string
    message: string
    timestamp?: string
    formattedTime: string
    formattedDate: string
    formattedSource: string
    category: string
    stackTrace?: string
}
