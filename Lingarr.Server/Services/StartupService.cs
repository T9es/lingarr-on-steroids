using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class StartupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupService> _logger;
    private static readonly SemaphoreSlim _cleanupLock = new(1, 1);

    public StartupService(IServiceProvider serviceProvider, ILogger<StartupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the application by validating and updating integration settings for integration services.
    /// This method is part of the application startup process and ensures all required service
    /// configurations are properly set.
    /// </summary>
    /// <param name="cancellationToken">Allows for cancellation of the startup process.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        await ApplySettingsFromEnvironment(dbContext);
        await EnsureSettingsExist(dbContext, new Dictionary<string, string>
        {
            // Integration Status
            { SettingKeys.Integration.RadarrSettingsCompleted, "false" },
            { SettingKeys.Integration.SonarrSettingsCompleted, "false" },

            // Translation Core
            { SettingKeys.Translation.ServiceType, "localai" },
            { SettingKeys.Translation.MaxParallelTranslations, "1" },
            { SettingKeys.Translation.SourceLanguages, "[]" },
            { SettingKeys.Translation.TargetLanguages, "[]" },
            { SettingKeys.Translation.FixOverlappingSubtitles, "false" },
            { SettingKeys.Translation.SubtitleOutputMode, "match-source" },
            { SettingKeys.Translation.StripSubtitleFormatting, "false" },
            { SettingKeys.Translation.AddTranslatorInfo, "false" },
            { SettingKeys.Translation.IgnoreCaptions, "false" },
            { SettingKeys.Translation.LanguageSettingsVersion, "1" },

            // Batch Translation
            { SettingKeys.Translation.UseBatchTranslation, "true" },
            { SettingKeys.Translation.MaxBatchSize, "120" },
            { SettingKeys.Translation.EnableBatchFallback, "true" },
            { SettingKeys.Translation.MaxBatchSplitAttempts, "3" },
            { SettingKeys.Translation.BatchRetryMode, "deferred" },
            { SettingKeys.Translation.RepairContextRadius, "10" },
            { SettingKeys.Translation.RepairMaxRetries, "1" },

            // Tagging
            { SettingKeys.Translation.UseSubtitleTagging, "false" },
            { SettingKeys.Translation.RemoveLanguageTag, "false" },
            { SettingKeys.Translation.SubtitleTag, "[Lingarr]" },
            { SettingKeys.Translation.SubtitleTagShort, "-ai-" },
            { SettingKeys.Translation.CleanupOrphanedSubtitles, "false" },
            { SettingKeys.Translation.TranslateSupplementalSubtitles, "false" },

            // Request/Retry
            { SettingKeys.Translation.RequestTimeout, "15" },
            { SettingKeys.Translation.MaxRetries, "20" },
            { SettingKeys.Translation.RetryDelay, "5" },
            { SettingKeys.Translation.RetryDelayMultiplier, "2" },

            // AI Context
            { SettingKeys.Translation.AiContextPromptEnabled, "false" },
            { SettingKeys.Translation.AiContextBefore, "2" },
            { SettingKeys.Translation.AiContextAfter, "2" },

            // ASS/SSA Drawing cleanup
            { SettingKeys.Translation.StripAssDrawingCommands, "false" },
            { SettingKeys.Translation.CleanSourceAssDrawings, "false" },

            // Batch Context Wrapper
            { SettingKeys.Translation.BatchContextEnabled, "false" },
            { SettingKeys.Translation.BatchContextBefore, "3" },
            { SettingKeys.Translation.BatchContextAfter, "3" },

            // Provider specific defaults
            { SettingKeys.Translation.Chutes.RequestBuffer, "50" },
            { SettingKeys.Translation.NanoGpt.SubscriptionModelsOnly, "true" },
            { SettingKeys.Translation.NanoGpt.WeeklyTokenAllowance, "60000000" },
            { SettingKeys.Translation.NanoGpt.TokenReserve, "0" },
            { SettingKeys.Translation.NanoGpt.DailyUnitReserve, "0" },
            { SettingKeys.Translation.NanoGpt.MonthlyUnitReserve, "0" },
            { SettingKeys.Translation.Anthropic.Version, "2023-06-01" },

            // Automation
            { SettingKeys.Automation.AutomationEnabled, "false" },
            { SettingKeys.Automation.TranslationSchedule, "0 * * * *" },
            { SettingKeys.Automation.CustomSourceScanSchedule, "15 * * * *" },
            { SettingKeys.Automation.MaxTranslationsPerRun, "100" },
            { SettingKeys.Automation.MovieSchedule, "0 4 * * *" },
            { SettingKeys.Automation.ShowSchedule, "0 4 * * *" },
            { SettingKeys.Automation.MovieAgeThreshold, "0" },
            { SettingKeys.Automation.ShowAgeThreshold, "0" },

            // Upload workspace
            { SettingKeys.UploadWorkspace.StorageRoot, "/app/config/uploads" },
            { SettingKeys.UploadWorkspace.RetentionDays, "7" },
            { SettingKeys.UploadWorkspace.ReservedWorkerSlots, "0" },
            { SettingKeys.UploadWorkspace.MaxBatchSize, "100" },
            { SettingKeys.UploadWorkspace.MaxFileSizeBytes, "2147483648" },

            // Subtitle Extraction
            { SettingKeys.SubtitleExtraction.ExtractionMode, "on_demand" },
            { SettingKeys.SubtitleExtraction.OcrEnabled, "true" },
            { SettingKeys.SubtitleExtraction.OcrAutoQueue, "true" },
            { SettingKeys.SubtitleExtraction.OcrMinQualityScore, "80" },
            { SettingKeys.SubtitleExtraction.OcrLanguages, "auto" },
            { SettingKeys.SubtitleExtraction.OcrTranslationPromptEnabled, "true" },

            // Validation
            { SettingKeys.SubtitleValidation.ValidateSubtitles, "false" },
            { SettingKeys.SubtitleValidation.IntegrityValidationEnabled, "false" },
            { SettingKeys.SubtitleValidation.BulkIntegrityAutoQueue, "false" },
            { SettingKeys.SubtitleValidation.BulkIntegrityMaxAutoQueuePerRun, "25" },
            { SettingKeys.SubtitleValidation.MaxFileSizeBytes, "1048576" },
            { SettingKeys.SubtitleValidation.MaxSubtitleLength, "500" },
            { SettingKeys.SubtitleValidation.MinSubtitleLength, "2" },
            { SettingKeys.SubtitleValidation.MinDurationMs, "500" },
            { SettingKeys.SubtitleValidation.MaxDurationSecs, "10" },
            { SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded, "true" },

            // Default AI Prompt Safeguard
            { SettingKeys.Translation.AiPrompt, "Translate subtitles into natural, plain text. NEVER include ASS/SSA tags ({\\...}), HTML tags, or animation/karaoke markers in your output. If a line is purely an animation syllable (e.g., 'ha', 'na', 'te') or non-dialogue fragment, return an empty string for that position. Maintain the natural flow of speech and do NOT remove valid short dialogue like 'No!', 'Stop!', or single-word meanings." }
        });

        await CheckAndUpdateIntegrationSettings(dbContext, "radarr", [
            SettingKeys.Integration.RadarrUrl,
            SettingKeys.Integration.RadarrApiKey
        ]);

        await CheckAndUpdateIntegrationSettings(dbContext, "sonarr", [
            SettingKeys.Integration.SonarrUrl,
            SettingKeys.Integration.SonarrApiKey
        ]);

        // Clean up duplicate records from multi-instance migration
        await CleanupDuplicateRecords(dbContext);
        
        // Auto-recover media stuck in AwaitingSource due to previous indexing bug
        await FixStuckAwaitingSourceMedia(dbContext);

        // Ensure service_type is not empty
        var serviceType = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.Translation.ServiceType);
        if (serviceType == null)
        {
            dbContext.Settings.Add(new Setting { Key = SettingKeys.Translation.ServiceType, Value = "localai" });
            await dbContext.SaveChangesAsync();
        }
        else if (string.IsNullOrWhiteSpace(serviceType.Value))
        {
            serviceType.Value = "localai";
            await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Validates and updates completion status for integration settings of a specific service.
    /// </summary>
    /// <param name="dbContext">The database context for accessing settings.</param>
    /// <param name="serviceName">Name of the service being validated (e.g., "radarr", "sonarr").</param>
    /// <param name="requiredKeys">Array of setting keys that must be present and non-empty for the service.</param>
    private async Task CheckAndUpdateIntegrationSettings(LingarrDbContext dbContext, string serviceName, string[] requiredKeys)
    {
        string completedKey = serviceName == "radarr"
            ? SettingKeys.Integration.RadarrSettingsCompleted
            : SettingKeys.Integration.SonarrSettingsCompleted;

        var settings = await dbContext.Settings
            .Where(s => requiredKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        bool allRequiredKeysHaveValues = requiredKeys.All(key =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value));

        if (allRequiredKeysHaveValues)
        {
            var setting = await dbContext.Settings.FindAsync(completedKey);
            if (setting != null)
            {
                setting.Value = "true";
                await dbContext.SaveChangesAsync();
                _logger.LogInformation($"{serviceName} settings completed.");
            }
        }
    }

    private static async Task EnsureSettingsExist(LingarrDbContext dbContext, IReadOnlyDictionary<string, string> defaults)
    {
        var existingKeys = await dbContext.Settings
            .Where(s => defaults.Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s);

        foreach (var entry in defaults)
        {
            if (existingKeys.ContainsKey(entry.Key))
            {
                continue;
            }

            dbContext.Settings.Add(new Setting
            {
                Key = entry.Key,
                Value = entry.Value
            });
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Applies settings from environment variables to the database.
    /// </summary>
    /// <param name="dbContext">The database context used to access and update settings.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ApplySettingsFromEnvironment(LingarrDbContext dbContext)
    {
        var environmentSettings = new Dictionary<string, string>
        {
            { "RADARR_URL", SettingKeys.Integration.RadarrUrl },
            { "RADARR_API_KEY", SettingKeys.Integration.RadarrApiKey },
            { "SONARR_URL", SettingKeys.Integration.SonarrUrl },
            { "SONARR_API_KEY", SettingKeys.Integration.SonarrApiKey },
            { "SOURCE_LANGUAGES", SettingKeys.Translation.SourceLanguages },
            { "TARGET_LANGUAGES", SettingKeys.Translation.TargetLanguages },

            { "SERVICE_TYPE", SettingKeys.Translation.ServiceType },
            { "MAX_PARALLEL_TRANSLATIONS", SettingKeys.Translation.MaxParallelTranslations },
            { "LIBRE_TRANSLATE_URL", SettingKeys.Translation.LibreTranslate.Url },
            { "LIBRE_TRANSLATE_API_KEY", SettingKeys.Translation.LibreTranslate.ApiKey },
            { "AI_PROMPT", SettingKeys.Translation.AiPrompt },

            { "OPENAI_MODEL", SettingKeys.Translation.OpenAi.Model },
            { "OPENAI_API_KEY", SettingKeys.Translation.OpenAi.ApiKey },

            { "ANTHROPIC_MODEL", SettingKeys.Translation.Anthropic.Model },
            { "ANTHROPIC_API_KEY", SettingKeys.Translation.Anthropic.ApiKey },
            { "ANTHROPIC_VERSION", SettingKeys.Translation.Anthropic.Version },

            { "LOCAL_AI_MODEL", SettingKeys.Translation.LocalAi.Model },
            { "LOCAL_AI_API_KEY", SettingKeys.Translation.LocalAi.ApiKey },
            { "LOCAL_AI_ENDPOINT", SettingKeys.Translation.LocalAi.Endpoint },

            { "GEMINI_MODEL", SettingKeys.Translation.Gemini.Model },
            { "GEMINI_API_KEY", SettingKeys.Translation.Gemini.ApiKey },

            { "DEEPSEEK_MODEL", SettingKeys.Translation.DeepSeek.Model },
            { "DEEPSEEK_API_KEY", SettingKeys.Translation.DeepSeek.ApiKey },

            { "DEEPL_API_KEY", SettingKeys.Translation.DeepL.DeeplApiKey },

            { "CHUTES_MODEL", SettingKeys.Translation.Chutes.Model },
            { "CHUTES_API_KEY", SettingKeys.Translation.Chutes.ApiKey },
            { "CHUTES_USAGE_LIMIT_OVERRIDE", SettingKeys.Translation.Chutes.UsageLimitOverride },

            { "NANOGPT_MODEL", SettingKeys.Translation.NanoGpt.Model },
            { "NANOGPT_API_KEY", SettingKeys.Translation.NanoGpt.ApiKey },
            { "NANOGPT_SUBSCRIPTION_MODELS_ONLY", SettingKeys.Translation.NanoGpt.SubscriptionModelsOnly },
            { "NANOGPT_WEEKLY_TOKEN_ALLOWANCE", SettingKeys.Translation.NanoGpt.WeeklyTokenAllowance },
            { "NANOGPT_TOKEN_RESERVE", SettingKeys.Translation.NanoGpt.TokenReserve },
            { "NANOGPT_DAILY_UNIT_RESERVE", SettingKeys.Translation.NanoGpt.DailyUnitReserve },
            { "NANOGPT_MONTHLY_UNIT_RESERVE", SettingKeys.Translation.NanoGpt.MonthlyUnitReserve },

            { "CROFAI_MODEL", SettingKeys.Translation.CrofAi.Model },
            { "CROFAI_API_KEY", SettingKeys.Translation.CrofAi.ApiKey }
        };

        foreach (var (envVar, settingKey) in environmentSettings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == settingKey);
                if (setting == null)
                {
                    setting = new Setting
                    {
                        Key = settingKey,
                        Value = value
                    };
                    dbContext.Settings.Add(setting);
                    await dbContext.SaveChangesAsync();
                }
                else if (setting.Value != value)
                {
                    setting.Value = value;
                    await dbContext.SaveChangesAsync();
                }

                _logger.LogInformation($"Updated setting '{settingKey}' from environment variable '{envVar}'.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Cleans up duplicate movie and show records that may have been created
    /// during the multi-instance migration. This happens when users upgrade from
    /// a pre-multi-instance version where source_instance_id was NULL.
    /// </summary>
    private async Task CleanupDuplicateRecords(LingarrDbContext dbContext)
    {
        if (!await _cleanupLock.WaitAsync(0))
        {
            _logger.LogInformation("Cleanup already in progress, skipping");
            return;
        }

        try
        {
            // Delete duplicate movies - keep the one with lowest ID (oldest)
            // Group by both RadarrId AND SourceInstanceId to preserve legitimate multi-instance records
            var duplicateMovies = await dbContext.Movies
                .GroupBy(m => new { m.RadarrId, m.SourceInstanceId })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            if (duplicateMovies.Count > 0)
            {
                var moviesToDelete = new List<Movie>();
                foreach (var key in duplicateMovies)
                {
                    var duplicates = await dbContext.Movies
                        .Where(m => m.RadarrId == key.RadarrId && m.SourceInstanceId == key.SourceInstanceId)
                        .OrderByDescending(m => m.Id)
                        .Skip(1)
                        .ToListAsync();
                    moviesToDelete.AddRange(duplicates);
                }

                dbContext.Movies.RemoveRange(moviesToDelete);
                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} duplicate movie records from multi-instance migration.", moviesToDelete.Count);
            }

            // Delete duplicate shows - keep the one with lowest ID (oldest)
            // Group by both SonarrId AND SourceInstanceId to preserve legitimate multi-instance records
            var duplicateShows = await dbContext.Shows
                .GroupBy(s => new { s.SonarrId, s.SourceInstanceId })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            if (duplicateShows.Count > 0)
            {
                var showsToDelete = new List<Show>();
                foreach (var key in duplicateShows)
                {
                    var duplicates = await dbContext.Shows
                        .Where(s => s.SonarrId == key.SonarrId && s.SourceInstanceId == key.SourceInstanceId)
                        .OrderByDescending(s => s.Id)
                        .Skip(1)
                        .ToListAsync();
                    showsToDelete.AddRange(duplicates);
                }

                dbContext.Shows.RemoveRange(showsToDelete);
                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} duplicate show records from multi-instance migration.", showsToDelete.Count);
            }

            // Update remaining records to have 'default' as source_instance_id if NULL
            var moviesWithNullInstance = await dbContext.Movies
                .Where(m => m.SourceInstanceId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.SourceInstanceId, "default"));

            var showsWithNullInstance = await dbContext.Shows
                .Where(s => s.SourceInstanceId == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SourceInstanceId, "default"));

            if (moviesWithNullInstance > 0 || showsWithNullInstance > 0)
            {
                _logger.LogInformation("Updated {Movies} movies and {Shows} shows with NULL source_instance_id to 'default'.", 
                    moviesWithNullInstance, showsWithNullInstance);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate record cleanup. Continuing startup...");
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    /// <summary>
    /// Recovers media stuck in AwaitingSource due to indexing issues.
    /// Two scenarios are handled:
    /// 1. Movies with embedded subtitles recorded but state wasn't updated (previous bug)
    /// 2. Movies marked as indexed but with NO embedded subtitles (indexing failed silently)
    /// Clears their IndexedAt and State so the sync job will re-evaluate them.
    /// </summary>
    private async Task FixStuckAwaitingSourceMedia(LingarrDbContext dbContext)
    {
        try
        {
            // Case 1: Has embedded subtitles but state wasn't updated
            var moviesWithSubs = await dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .Where(m => m.TranslationState == TranslationState.AwaitingSource && m.IndexedAt != null)
                .Where(m => m.EmbeddedSubtitles.Any(e => e.IsTextBased))
                .ToListAsync();

            // Case 2: Indexed but NO embedded subtitles (indexing failed or records lost)
            var moviesWithoutSubs = await dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .Where(m => m.TranslationState == TranslationState.AwaitingSource && m.IndexedAt != null)
                .Where(m => !m.EmbeddedSubtitles.Any())
                .ToListAsync();

            var allAffectedMovies = moviesWithSubs.Concat(moviesWithoutSubs).ToList();

            if (allAffectedMovies.Count > 0)
            {
                foreach (var movie in allAffectedMovies)
                {
                    movie.IndexedAt = null;
                    movie.TranslationState = TranslationState.Unknown;
                }
                await dbContext.SaveChangesAsync();
                _logger.LogInformation(
                    "Reset state for {Count} movies stuck in AwaitingSource ({WithSubs} with subs, {WithoutSubs} without subs).",
                    allAffectedMovies.Count, moviesWithSubs.Count, moviesWithoutSubs.Count);
            }
            
            // Same for episodes
            var episodesWithSubs = await dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .Where(e => e.TranslationState == TranslationState.AwaitingSource && e.IndexedAt != null)
                .Where(e => e.EmbeddedSubtitles.Any(e => e.IsTextBased))
                .ToListAsync();

            var episodesWithoutSubs = await dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .Where(e => e.TranslationState == TranslationState.AwaitingSource && e.IndexedAt != null)
                .Where(e => !e.EmbeddedSubtitles.Any())
                .ToListAsync();

            var allAffectedEpisodes = episodesWithSubs.Concat(episodesWithoutSubs).ToList();

            if (allAffectedEpisodes.Count > 0)
            {
                foreach (var episode in allAffectedEpisodes)
                {
                    episode.IndexedAt = null;
                    episode.TranslationState = TranslationState.Unknown;
                }
                await dbContext.SaveChangesAsync();
                _logger.LogInformation(
                    "Reset state for {Count} episodes stuck in AwaitingSource ({WithSubs} with subs, {WithoutSubs} without subs).",
                    allAffectedEpisodes.Count, episodesWithSubs.Count, episodesWithoutSubs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AwaitingSource stuck media recovery. Continuing startup...");
        }
    }
}
