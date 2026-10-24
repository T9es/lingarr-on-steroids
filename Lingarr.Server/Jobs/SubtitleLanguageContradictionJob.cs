using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Lingarr.Server.Jobs;

/// <summary>
/// Bounded daily job that re-checks a capped sample of AwaitingSource/Failed media whose
/// embedded subtitle streams may be tagged with the wrong language. Detected contradictions
/// are persisted on the subtitle row and the media is re-processed once so a mislabeled full
/// dialogue track is adopted instead of the item waiting or failing forever.
/// </summary>
public class SubtitleLanguageContradictionJob
{
    public const int DefaultMaxItemsPerRun = 25;

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleLanguageContradictionDetector _languageContradictionDetector;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ILogger<SubtitleLanguageContradictionJob> _logger;

    public SubtitleLanguageContradictionJob(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleLanguageContradictionDetector languageContradictionDetector,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ILogger<SubtitleLanguageContradictionJob> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _languageContradictionDetector = languageContradictionDetector;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var automationEnabled = await _settingService.GetSetting(SettingKeys.Automation.AutomationEnabled);
        if (automationEnabled != "true")
        {
            _logger.LogInformation("Automation is disabled, skipping subtitle language contradiction job");
            return;
        }

        var maxItems = await GetMaxItemsAsync();
        if (maxItems <= 0)
        {
            _logger.LogInformation("Subtitle language contradiction check has no item budget; skipping");
            return;
        }

        var candidates = await LoadCandidatesAsync(maxItems);
        if (candidates.Count == 0)
        {
            _logger.LogInformation("No AwaitingSource/Failed media with embedded subtitle tracks need a language check");
            return;
        }

        var checkedStreams = 0;
        var contradictions = 0;
        var requeued = 0;
        var unchangedContradictions = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                var mediaPath = MediaFileResolver.Resolve(candidate.Path, candidate.FileName);
                if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
                {
                    _logger.LogDebug(
                        "Skipping language check for {MediaType} {MediaId}: media file not found",
                        candidate.MediaType,
                        candidate.MediaId);
                    continue;
                }

                var readableStreams = candidate.EmbeddedSubtitles
                    .Where(subtitle => subtitle.IsReadableSource())
                    .Where(subtitle => !string.IsNullOrWhiteSpace(subtitle.Language))
                    .ToList();
                if (readableStreams.Count == 0)
                {
                    continue;
                }

                // Snapshot the effective language before the new check overwrites the row so a
                // repeated contradiction for an unchanged stream is not requeued every run.
                var previousEffectiveLanguages = readableStreams.ToDictionary(
                    subtitle => subtitle.StreamIndex,
                    subtitle => SubtitleLanguageHelper.GetEffectiveLanguage(subtitle));

                var requests = readableStreams
                    .Select(subtitle => new SubtitleLanguageCheckRequest(subtitle, mediaPath))
                    .ToList();
                var outcomes = await _languageContradictionDetector.CheckBatchAsync(requests);
                checkedStreams += outcomes.Count;

                var adopted = outcomes
                    .Where(outcome => outcome.Persisted)
                    .Where(outcome => outcome.Verdict == SubtitleLanguageVerdict.Contradiction)
                    .Where(outcome => outcome.Confidence >= SubtitleLanguageContradictionDetector.MinimumAdoptedConfidence)
                    .OrderByDescending(outcome => outcome.Confidence)
                    .ThenBy(outcome => outcome.Subtitle.StreamIndex)
                    .FirstOrDefault();
                if (adopted == null)
                {
                    continue;
                }

                contradictions++;
                _logger.LogInformation(
                    "Language contradiction found for {MediaType} {MediaId}: stream {StreamIndex} declared '{Declared}' detected '{Detected}' (confidence {Confidence:F2})",
                    candidate.MediaType,
                    candidate.MediaId,
                    adopted.Subtitle.StreamIndex,
                    adopted.DeclaredLanguage,
                    adopted.DetectedLanguage,
                    adopted.Confidence);

                previousEffectiveLanguages.TryGetValue(
                    adopted.Subtitle.StreamIndex,
                    out var previousEffectiveLanguage);
                var currentEffectiveLanguage = SubtitleLanguageHelper.GetEffectiveLanguage(adopted.Subtitle);
                if (!ShouldRequeueAfterAdoption(
                        previousEffectiveLanguage,
                        adopted.Subtitle.Language,
                        currentEffectiveLanguage))
                {
                    unchangedContradictions++;
                    _logger.LogInformation(
                        "Language contradiction for {MediaType} {MediaId} stream {StreamIndex} is unchanged since the previous check; not requeueing",
                        candidate.MediaType,
                        candidate.MediaId,
                        adopted.Subtitle.StreamIndex);
                    continue;
                }

                var media = candidate.MediaType == MediaType.Movie
                    ? (IMedia?)await _dbContext.Movies.FindAsync(candidate.MediaId)
                    : await _dbContext.Episodes.FindAsync(candidate.MediaId);
                if (media == null || string.IsNullOrWhiteSpace(media.Path))
                {
                    continue;
                }

                var queued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                    media,
                    candidate.MediaType,
                    forceProcess: true,
                    forceTranslation: false,
                    forcePriority: false,
                    forceRequeue: true);
                if (queued > 0)
                {
                    requeued += queued;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Subtitle language contradiction check failed for {MediaType} {MediaId}",
                    candidate.MediaType,
                    candidate.MediaId);
            }
        }

        _logger.LogInformation(
            "Subtitle language contradiction job completed: {MediaCount} media checked, {StreamCount} streams checked, " +
            "{ContradictionCount} contradictions adopted, {UnchangedCount} unchanged, {RequeuedCount} translation request(s) queued",
            candidates.Count,
            checkedStreams,
            contradictions,
            unchangedContradictions,
            requeued);
    }

    /// <summary>
    /// A media item is requeued only when the adoption changes something: either the stream
    /// had no known contradiction before this check, or the effective detected language
    /// changed. Repeatedly requeueing an unchanged contradiction every cache expiry creates
    /// duplicate failed request rows without repairing anything.
    /// </summary>
    internal static bool ShouldRequeueAfterAdoption(
        string? previousEffectiveLanguage,
        string? declaredLanguage,
        string? currentEffectiveLanguage)
    {
        if (string.IsNullOrWhiteSpace(currentEffectiveLanguage))
        {
            return true;
        }

        var declared = SubtitleLanguageHelper.NormalizeLanguageCode(declaredLanguage);
        var previousWasContradiction = !string.IsNullOrWhiteSpace(previousEffectiveLanguage) &&
                                       !string.IsNullOrWhiteSpace(declared) &&
                                       !string.Equals(
                                           previousEffectiveLanguage,
                                           declared,
                                           StringComparison.OrdinalIgnoreCase);
        if (!previousWasContradiction)
        {
            return true;
        }

        return !string.Equals(
            previousEffectiveLanguage,
            currentEffectiveLanguage,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> GetMaxItemsAsync()
    {
        var value = await _settingService.GetSetting(SettingKeys.Maintenance.SubtitleLanguageCheckMaxItems);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : DefaultMaxItemsPerRun;
    }

    private async Task<List<LanguageCheckCandidate>> LoadCandidatesAsync(int maxItems)
    {
        // Pending is included because the nightly retry deletes permanently failed rows and
        // leaves deterministic source failures Pending, which would otherwise only be
        // re-attempted by automation with the same wrong tag.
        var wantedStates = new[]
        {
            TranslationState.AwaitingSource,
            TranslationState.Failed,
            TranslationState.Pending
        };

        var now = DateTime.UtcNow;
        var needsLanguageCheck = BuildNeedsLanguageCheckExpression(
            _languageContradictionDetector.CurrentVersion,
            now - SubtitleLanguageContradictionDetector.PositiveVerdictCache,
            now - SubtitleLanguageContradictionDetector.NegativeVerdictCache);

        var halfLimit = Math.Max(maxItems / 2, 1);
        var movieRows = await LoadStaleMovieRowsAsync(halfLimit, wantedStates, needsLanguageCheck);
        var episodeRows = await LoadStaleEpisodeRowsAsync(
            Math.Max(maxItems - movieRows.Count, 0),
            wantedStates,
            needsLanguageCheck);

        var candidates = new List<LanguageCheckCandidate>(movieRows.Count + episodeRows.Count);
        candidates.AddRange(movieRows.Select(movie => new LanguageCheckCandidate(
            movie.Id,
            MediaType.Movie,
            movie.Path,
            movie.FileName,
            movie.EmbeddedSubtitles)));
        candidates.AddRange(episodeRows.Select(episode => new LanguageCheckCandidate(
            episode.Id,
            MediaType.Episode,
            episode.Path,
            episode.FileName,
            episode.EmbeddedSubtitles)));

        return candidates;
    }

    /// <summary>
    /// Fresh verdicts are excluded before the item cap is applied. Ordering uses the oldest
    /// stored subtitle check time per media, so a rechecked media moves behind the backlog
    /// instead of the same oldest rows consuming the budget on every run.
    /// </summary>
    private async Task<List<Movie>> LoadStaleMovieRowsAsync(
        int limit,
        TranslationState[] wantedStates,
        Expression<Func<EmbeddedSubtitle, bool>> needsLanguageCheck)
    {
        if (limit <= 0)
        {
            return [];
        }

        var eligibleMovieIds = _dbContext.Movies
            .Where(movie => !movie.ExcludeFromTranslation)
            .Where(movie => wantedStates.Contains(movie.TranslationState))
            .Select(movie => movie.Id);

        var staleChecks = await _dbContext.EmbeddedSubtitles
            .Where(subtitle => subtitle.MovieId.HasValue)
            .Where(needsLanguageCheck)
            .Where(subtitle => eligibleMovieIds.Contains(subtitle.MovieId.GetValueOrDefault()))
            .Select(subtitle => new { subtitle.MovieId, subtitle.LanguageCheckedAt })
            .ToListAsync();

        var orderedIds = staleChecks
            .GroupBy(entry => entry.MovieId.GetValueOrDefault())
            .Select(group => new
            {
                MediaId = group.Key,
                OldestCheck = group.Min(entry => entry.LanguageCheckedAt) ?? DateTime.MinValue
            })
            .OrderBy(entry => entry.OldestCheck)
            .ThenBy(entry => entry.MediaId)
            .Take(limit)
            .Select(entry => entry.MediaId)
            .ToList();

        return await LoadMoviesInOrderAsync(orderedIds);
    }

    private async Task<List<Episode>> LoadStaleEpisodeRowsAsync(
        int limit,
        TranslationState[] wantedStates,
        Expression<Func<EmbeddedSubtitle, bool>> needsLanguageCheck)
    {
        if (limit <= 0)
        {
            return [];
        }

        var eligibleEpisodeIds = _dbContext.Episodes
            .Where(episode => !episode.ExcludeFromTranslation)
            .Where(episode => wantedStates.Contains(episode.TranslationState))
            .Select(episode => episode.Id);

        var staleChecks = await _dbContext.EmbeddedSubtitles
            .Where(subtitle => subtitle.EpisodeId.HasValue)
            .Where(needsLanguageCheck)
            .Where(subtitle => eligibleEpisodeIds.Contains(subtitle.EpisodeId.GetValueOrDefault()))
            .Select(subtitle => new { subtitle.EpisodeId, subtitle.LanguageCheckedAt })
            .ToListAsync();

        var orderedIds = staleChecks
            .GroupBy(entry => entry.EpisodeId.GetValueOrDefault())
            .Select(group => new
            {
                MediaId = group.Key,
                OldestCheck = group.Min(entry => entry.LanguageCheckedAt) ?? DateTime.MinValue
            })
            .OrderBy(entry => entry.OldestCheck)
            .ThenBy(entry => entry.MediaId)
            .Take(limit)
            .Select(entry => entry.MediaId)
            .ToList();

        return await LoadEpisodesInOrderAsync(orderedIds);
    }

    private async Task<List<Movie>> LoadMoviesInOrderAsync(IReadOnlyList<int> orderedIds)
    {
        if (orderedIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.Movies
            .Include(movie => movie.EmbeddedSubtitles)
            .Where(movie => orderedIds.Contains(movie.Id))
            .ToListAsync();

        var byId = rows.ToDictionary(movie => movie.Id);
        return orderedIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    private async Task<List<Episode>> LoadEpisodesInOrderAsync(IReadOnlyList<int> orderedIds)
    {
        if (orderedIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.Episodes
            .Include(episode => episode.EmbeddedSubtitles)
            .Where(episode => orderedIds.Contains(episode.Id))
            .ToListAsync();

        var byId = rows.ToDictionary(episode => episode.Id);
        return orderedIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    /// <summary>
    /// A text-based subtitle needs a check when it was never checked, was checked with an
    /// older algorithm version, or its verdict left the cache window. Positive verdicts are
    /// cached for a week and negative ones for six hours, matching the detector.
    /// </summary>
    private static Expression<Func<EmbeddedSubtitle, bool>> BuildNeedsLanguageCheckExpression(
        int currentVersion,
        DateTime positiveCutoff,
        DateTime negativeCutoff)
    {
        return subtitle =>
            subtitle.IsTextBased &&
            (subtitle.LanguageCheckedAt == null ||
             subtitle.LanguageDetectionVersion != currentVersion ||
             ((subtitle.DetectedLanguage != null &&
               subtitle.DetectedLanguageConfidence >= SubtitleLanguageHelper.MinimumEffectiveLanguageConfidence)
                 ? subtitle.LanguageCheckedAt < positiveCutoff
                 : subtitle.LanguageCheckedAt < negativeCutoff));
    }

    private sealed record LanguageCheckCandidate(
        int MediaId,
        MediaType MediaType,
        string? Path,
        string? FileName,
        List<EmbeddedSubtitle> EmbeddedSubtitles);
}
