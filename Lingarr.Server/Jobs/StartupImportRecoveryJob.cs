using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Models.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Recovers episode imports whose Sonarr download webhook never reached Lingarr, which
/// happens when Sonarr imports an episode while the application is restarting. Without
/// this pass those episodes stay invisible until the next full show sync, so nothing
/// indexes their embedded subtitles and nothing queues a translation.
/// </summary>
public class StartupImportRecoveryJob
{
    /// <summary>
    /// How far back the Sonarr history is inspected. The daily full show sync remains the
    /// backstop for anything older.
    /// </summary>
    internal static readonly TimeSpan RecoveryWindow = TimeSpan.FromHours(48);

    internal const int HistoryPageSize = 100;
    internal const int MaxHistoryPages = 5;
    internal const int MaxRecoveredEpisodes = 200;

    private readonly ISonarrService _sonarrService;
    private readonly IInstanceConfigService _instanceConfigService;
    private readonly LingarrDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<StartupImportRecoveryJob> _logger;

    public StartupImportRecoveryJob(
        ISonarrService sonarrService,
        IInstanceConfigService instanceConfigService,
        LingarrDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        ILogger<StartupImportRecoveryJob> logger)
    {
        _sonarrService = sonarrService;
        _instanceConfigService = instanceConfigService;
        _dbContext = dbContext;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 2)]
    [Queue("system")]
    public async Task Execute()
    {
        var instances = await _instanceConfigService.GetSonarrConfigs();
        if (instances.Count == 0)
        {
            _logger.LogInformation("Startup import recovery skipped: no Sonarr instances configured");
            return;
        }

        var since = DateTime.UtcNow - RecoveryWindow;
        var recovered = 0;

        foreach (var instance in instances)
        {
            var remaining = MaxRecoveredEpisodes - recovered;
            if (remaining <= 0)
            {
                break;
            }

            try
            {
                recovered += await RecoverInstanceAsync(instance, since, remaining);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Startup import recovery failed for Sonarr instance '{InstanceId}'",
                    instance.InstanceId);
            }
        }

        _logger.LogInformation(
            "Startup import recovery finished: {Recovered} missed episode import(s) recovered",
            recovered);
    }

    private async Task<int> RecoverInstanceAsync(InstanceConfig instance, DateTime since, int maxEpisodes)
    {
        var imports = await LoadRecentImportsAsync(instance, since);
        if (imports.Count == 0)
        {
            return 0;
        }

        var episodeIds = imports.Select(import => import.EpisodeId).Distinct().ToList();
        var existingEpisodes = await _dbContext.Episodes
            .Where(episode => episode.SourceInstanceId == instance.InstanceId &&
                              episodeIds.Contains(episode.SonarrId))
            .Select(episode => new { episode.SonarrId, episode.MissingSince })
            .ToListAsync();

        // A row that is hidden from automation because Sonarr previously reported the file as
        // missing must be replayed too: a real webhook would clear MissingSince and queue the
        // translation again.
        var presentEpisodeIds = existingEpisodes
            .Where(episode => episode.MissingSince == null)
            .Select(episode => episode.SonarrId)
            .ToHashSet();
        var episodeIdsToRecover = episodeIds
            .Where(episodeId => !presentEpisodeIds.Contains(episodeId))
            .Take(maxEpisodes)
            .ToHashSet();
        if (episodeIdsToRecover.Count == 0)
        {
            return 0;
        }

        var recovered = 0;
        foreach (var seriesImports in imports
                     .Where(import => episodeIdsToRecover.Contains(import.EpisodeId))
                     .GroupBy(import => import.SeriesId))
        {
            // WebhookJob resolves the authoritative episode from Sonarr by id, so the payload
            // only needs ids; the remaining episode fields are informational.
            var episodes = seriesImports
                .Select(import => import.EpisodeId)
                .Distinct()
                .Select(episodeId => new SonarrWebhookEpisode { Id = episodeId })
                .ToList();
            if (episodes.Count == 0)
            {
                continue;
            }

            var series = seriesImports.Select(import => import.Series).FirstOrDefault(series => series != null);
            var payload = new SonarrWebhookPayload
            {
                EventType = "Download",
                Series = new SonarrWebhookSeries
                {
                    Id = seriesImports.Key,
                    Title = series?.Title ?? string.Empty,
                    Path = series?.Path,
                    TvdbId = series?.TvdbId
                },
                Episodes = episodes
            };

            _backgroundJobClient.Enqueue<WebhookJob>(job =>
                job.ProcessSonarrWebhook(payload, instance.InstanceId));

            recovered += episodes.Count;
            _logger.LogInformation(
                "Recovering {Count} missed Sonarr import(s) for '{SeriesTitle}' (ID: {SeriesId}) from instance '{InstanceId}'",
                episodes.Count,
                payload.Series.Title,
                seriesImports.Key,
                instance.InstanceId);
        }

        return recovered;
    }

    private async Task<List<MissedImport>> LoadRecentImportsAsync(InstanceConfig instance, DateTime since)
    {
        var imports = new List<MissedImport>();
        var seenEpisodeIds = new HashSet<int>();
        var reachedHistoryEnd = false;

        for (var page = 1; page <= MaxHistoryPages; page++)
        {
            var history = await _sonarrService.GetImportHistory(
                page,
                HistoryPageSize,
                instance.Url,
                instance.ApiKey);
            if (history == null)
            {
                _logger.LogWarning(
                    "Could not read Sonarr import history for instance '{InstanceId}', page {Page}",
                    instance.InstanceId,
                    page);
                reachedHistoryEnd = true;
                break;
            }

            if (history.Records.Count == 0)
            {
                reachedHistoryEnd = true;
                break;
            }

            foreach (var record in history.Records)
            {
                if (record.EpisodeId <= 0 || record.Date.ToUniversalTime() < since)
                {
                    continue;
                }

                if (!seenEpisodeIds.Add(record.EpisodeId))
                {
                    continue;
                }

                imports.Add(new MissedImport(record.EpisodeId, record.SeriesId, record.Series));
            }

            var oldestRecord = history.Records[^1].Date.ToUniversalTime();
            if (history.Records.Count < HistoryPageSize || oldestRecord < since)
            {
                reachedHistoryEnd = true;
                break;
            }
        }

        if (!reachedHistoryEnd)
        {
            _logger.LogWarning(
                "Sonarr import history for instance '{InstanceId}' was not read up to the {Window} window within {Pages} pages; recovery may be incomplete",
                instance.InstanceId,
                RecoveryWindow,
                MaxHistoryPages);
        }

        return imports;
    }

    private sealed record MissedImport(int EpisodeId, int SeriesId, SonarrHistorySeries? Series);
}
