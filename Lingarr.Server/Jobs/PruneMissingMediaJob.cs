using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Prunes media rows that Sonarr/Radarr reported missing longer than the configured grace
/// window. Only rows without requests, embedded subtitles, diagnostics or per-item
/// settings are deleted; anything with history stays visible and is skipped.
/// Window setting: prune_missing_media_days, default 7, 0 disables pruning.
///
/// Keep a database backup before the first live run:
/// PostgreSQL: pg_dump -U &lt;user&gt; -d &lt;database&gt; -f lingarr-before-prune.sql
/// SQLite: stop Lingarr first, then copy the configured database file.
/// The dry run does not delete anything: enqueue PruneMissingMediaJob.ExecuteDryRun from
/// the Hangfire dashboard to see exactly which rows would be pruned.
/// </summary>
public class PruneMissingMediaJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly IScheduleService _scheduleService;
    private readonly ILogger<PruneMissingMediaJob> _logger;

    public PruneMissingMediaJob(
        LingarrDbContext dbContext,
        ISettingService settingService,
        IScheduleService scheduleService,
        ILogger<PruneMissingMediaJob> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    /// <summary>
    /// Scheduled daily prune. Deletes eligible rows whose missing marker is older than the
    /// configured grace window. Bounded per run by <see cref="MissingMediaPrunePolicy.MaxPruneBatchSize"/>.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 15 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public Task Execute()
    {
        return ExecuteInternalAsync(dryRun: false);
    }

    /// <summary>
    /// Manual dry run: reports the rows that would be pruned without deleting anything.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 15 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public Task ExecuteDryRun()
    {
        return ExecuteInternalAsync(dryRun: true);
    }

    internal async Task<PruneMissingMediaResult> ExecuteInternalAsync(
        bool dryRun,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await UpdateJobStateAsync(jobName, JobStatus.Processing);

        try
        {
            var windowDays = MissingMediaPrunePolicy.ResolvePruneWindowDays(
                await _settingService.GetSetting(SettingKeys.Maintenance.PruneMissingMediaDays));
            if (windowDays <= 0)
            {
                _logger.LogInformation(
                    "Missing media pruning is disabled because {Setting} is 0 or lower.",
                    SettingKeys.Maintenance.PruneMissingMediaDays);
                await UpdateJobStateAsync(jobName, JobStatus.Succeeded);
                return new PruneMissingMediaResult(0, 0, 0, 0, dryRun, windowDays);
            }

            var cutoff = DateTime.UtcNow.AddDays(-windowDays);
            var batchSize = Math.Clamp(
                maxItems ?? MissingMediaPrunePolicy.MaxPruneBatchSize,
                1,
                MissingMediaPrunePolicy.MaxPruneBatchSize);

            var movies = await _dbContext.Movies
                .Where(movie => movie.MissingSince != null && movie.MissingSince <= cutoff)
                .OrderBy(movie => movie.MissingSince)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var episodes = await _dbContext.Episodes
                .Include(episode => episode.Season)
                .ThenInclude(season => season.Show)
                .Where(episode => episode.MissingSince != null && episode.MissingSince <= cutoff)
                .OrderBy(episode => episode.MissingSince)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var moviesPruned = await PruneMoviesAsync(movies, cutoff, dryRun, cancellationToken);
            var episodesPruned = await PruneEpisodesAsync(episodes, cutoff, dryRun, cancellationToken);

            if (!dryRun && _dbContext.ChangeTracker.HasChanges())
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var keptVisible = movies.Count - moviesPruned + episodes.Count - episodesPruned;
            var result = new PruneMissingMediaResult(
                moviesPruned,
                episodesPruned,
                keptVisible,
                movies.Count + episodes.Count,
                dryRun,
                windowDays);

            _logger.LogInformation(
                "Missing media prune {Mode}: window {WindowDays} day(s), checked {Candidates} candidate(s), {Deleted} row(s) {DeleteVerb}, {KeptVisible} kept visible because they carry history or per-item settings.",
                dryRun ? "dry run" : "run",
                windowDays,
                result.CandidatesChecked,
                result.MoviesPruned + result.EpisodesPruned,
                dryRun ? "would be pruned" : "pruned",
                result.RowsKeptVisible);

            await UpdateJobStateAsync(jobName, JobStatus.Succeeded);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Missing media prune failed.");
            await UpdateJobStateAsync(jobName, JobStatus.Failed);
            throw;
        }
    }

    private async Task<int> PruneMoviesAsync(
        IReadOnlyCollection<Movie> candidates,
        DateTime cutoff,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var pruned = 0;
        foreach (var movie in candidates)
        {
            if (movie.MissingSince == null || movie.MissingSince > cutoff)
            {
                continue;
            }

            if (!await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(
                    _dbContext,
                    movie,
                    cancellationToken))
            {
                // The row gained history or settings, so make it visible again instead of deleting it.
                if (!dryRun)
                {
                    movie.MissingSince = null;
                }

                continue;
            }

            if (dryRun)
            {
                pruned++;
                _logger.LogInformation(
                    "[Dry run] Would prune missing movie {Title} (ID {Id}), missing since {MissingSince:O}.",
                    movie.Title,
                    movie.Id,
                    movie.MissingSince);
                continue;
            }

            _dbContext.Movies.Remove(movie);
            pruned++;
        }

        return pruned;
    }

    private async Task<int> PruneEpisodesAsync(
        IReadOnlyCollection<Episode> candidates,
        DateTime cutoff,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var pruned = 0;
        foreach (var episode in candidates)
        {
            if (episode.MissingSince == null || episode.MissingSince > cutoff)
            {
                continue;
            }

            if (!await MissingMediaPrunePolicy.IsEligibleForMissingTrackingAsync(
                    _dbContext,
                    episode,
                    cancellationToken))
            {
                // The row gained history or settings, so make it visible again instead of deleting it.
                if (!dryRun)
                {
                    episode.MissingSince = null;
                }

                continue;
            }

            if (dryRun)
            {
                pruned++;
                _logger.LogInformation(
                    "[Dry run] Would prune missing episode {Title} (ID {Id}), missing since {MissingSince:O}.",
                    episode.Title,
                    episode.Id,
                    episode.MissingSince);
                continue;
            }

            _dbContext.Episodes.Remove(episode);
            pruned++;
        }

        return pruned;
    }

    private async Task UpdateJobStateAsync(string jobName, JobStatus status)
    {
        if (string.IsNullOrEmpty(jobName))
        {
            return;
        }

        await _scheduleService.UpdateJobState(jobName, status.GetDisplayName());
    }
}

/// <summary>
/// Outcome of a single missing media prune run.
/// </summary>
internal sealed record PruneMissingMediaResult(
    int MoviesPruned,
    int EpisodesPruned,
    int RowsKeptVisible,
    int CandidatesChecked,
    bool DryRun,
    int WindowDays);
