using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class UnknownLanguageDetectionJob
{
    private readonly ISubtitleLanguageDetectionService _languageDetectionService;
    private readonly ISettingService _settingService;
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<UnknownLanguageDetectionJob> _logger;

    public UnknownLanguageDetectionJob(
        ISubtitleLanguageDetectionService languageDetectionService,
        ISettingService settingService,
        LingarrDbContext dbContext,
        ILogger<UnknownLanguageDetectionJob> logger)
    {
        _languageDetectionService = languageDetectionService;
        _settingService = settingService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    [Queue("system")]
    public async Task Execute()
    {
        _logger.LogInformation("Unknown language detection job started");

        var detectEnabled = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleExtraction.DetectUnknownLanguages),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!detectEnabled)
        {
            _logger.LogDebug("Unknown language detection is disabled; skipping scheduled job");
            return;
        }

        var movieIds = await _dbContext.EmbeddedSubtitles
            .Where(s => s.MovieId != null)
            .Where(s => s.IsTextBased)
            .Where(s => s.Language == null || s.Language == "" || s.Language == "und")
            .Select(s => s.MovieId!.Value)
            .Distinct()
            .ToListAsync();

        var episodeIds = await _dbContext.EmbeddedSubtitles
            .Where(s => s.EpisodeId != null)
            .Where(s => s.IsTextBased)
            .Where(s => s.Language == null || s.Language == "" || s.Language == "und")
            .Select(s => s.EpisodeId!.Value)
            .Distinct()
            .ToListAsync();

        _logger.LogInformation(
            "Found {MovieCount} movies and {EpisodeCount} episodes with untagged subtitle streams",
            movieIds.Count, episodeIds.Count);

        var totalDetected = 0;

        foreach (var movieId in movieIds)
        {
            try
            {
                var detected = await _languageDetectionService.DetectUnknownLanguagesAsync(
                    movieId: movieId);
                totalDetected += detected;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Language detection failed for movie {MovieId}", movieId);
            }
        }

        foreach (var episodeId in episodeIds)
        {
            try
            {
                var detected = await _languageDetectionService.DetectUnknownLanguagesAsync(
                    episodeId: episodeId);
                totalDetected += detected;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Language detection failed for episode {EpisodeId}", episodeId);
            }
        }

        _logger.LogInformation(
            "Unknown language detection job completed: {TotalDetected} streams identified",
            totalDetected);
    }
}