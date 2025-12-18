using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class StartupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupService> _logger;

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
            { SettingKeys.Translation.StripSubtitleFormatting, "false" },
            { SettingKeys.Translation.AddTranslatorInfo, "false" },
            { SettingKeys.Translation.IgnoreCaptions, "false" },
            { SettingKeys.Translation.LanguageSettingsVersion, "1" },

            // Batch Translation
            { SettingKeys.Translation.UseBatchTranslation, "true" },
            { SettingKeys.Translation.MaxBatchSize, "180" },
            { SettingKeys.Translation.EnableBatchFallback, "true" },
            { SettingKeys.Translation.MaxBatchSplitAttempts, "3" },
            { SettingKeys.Translation.BatchRetryMode, "deferred" },
            { SettingKeys.Translation.RepairContextRadius, "10" },
            { SettingKeys.Translation.RepairMaxRetries, "1" },

            // Tagging
            { SettingKeys.Translation.UseSubtitleTagging, "false" },
            { SettingKeys.Translation.RemoveLanguageTag, "false" },
            { SettingKeys.Translation.SubtitleTag, "[Lingarr]" },

            // Request/Retry
            { SettingKeys.Translation.RequestTimeout, "15" },
            { SettingKeys.Translation.MaxRetries, "20" },
            { SettingKeys.Translation.RetryDelay, "120" },
            { SettingKeys.Translation.RetryDelayMultiplier, "1" },

            // AI Context
            { SettingKeys.Translation.AiContextPromptEnabled, "false" },
            { SettingKeys.Translation.AiContextBefore, "2" },
            { SettingKeys.Translation.AiContextAfter, "2" },

            // ASS/SSA Drawing cleanup
            { SettingKeys.Translation.StripAssDrawingCommands, "false" },
            { SettingKeys.Translation.CleanSourceAssDrawings, "false" },

            // Provider specific defaults
            { SettingKeys.Translation.Chutes.RequestBuffer, "50" },
            { SettingKeys.Translation.Anthropic.Version, "2023-06-01" },

            // Automation
            { SettingKeys.Automation.AutomationEnabled, "false" },
            { SettingKeys.Automation.TranslationSchedule, "0 * * * *" },
            { SettingKeys.Automation.MaxTranslationsPerRun, "100" },
            { SettingKeys.Automation.MovieSchedule, "0 4 * * *" },
            { SettingKeys.Automation.ShowSchedule, "0 4 * * *" },
            { SettingKeys.Automation.MovieAgeThreshold, "0" },
            { SettingKeys.Automation.ShowAgeThreshold, "0" },

            // Subtitle Extraction
            { SettingKeys.SubtitleExtraction.ExtractionMode, "on_demand" },

            // Validation
            { SettingKeys.SubtitleValidation.ValidateSubtitles, "false" },
            { SettingKeys.SubtitleValidation.IntegrityValidationEnabled, "false" },
            { SettingKeys.SubtitleValidation.MaxFileSizeBytes, "1048576" },
            { SettingKeys.SubtitleValidation.MaxSubtitleLength, "500" },
            { SettingKeys.SubtitleValidation.MinSubtitleLength, "2" },
            { SettingKeys.SubtitleValidation.MinDurationMs, "500" },
            { SettingKeys.SubtitleValidation.MaxDurationSecs, "10" }
        });

        await CheckAndUpdateIntegrationSettings(dbContext, "radarr", [
            SettingKeys.Integration.RadarrUrl,
            SettingKeys.Integration.RadarrApiKey
        ]);

        await CheckAndUpdateIntegrationSettings(dbContext, "sonarr", [
            SettingKeys.Integration.SonarrUrl,
            SettingKeys.Integration.SonarrApiKey
        ]);

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
            { "CHUTES_USAGE_LIMIT_OVERRIDE", SettingKeys.Translation.Chutes.UsageLimitOverride }
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
}
