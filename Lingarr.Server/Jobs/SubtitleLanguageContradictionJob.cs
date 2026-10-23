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

                var requests = readableStreams
                    .Select(subtitle => new SubtitleLanguageCheckRequest(subtitle, mediaPath))
                    .ToList();
                var outcomes = await _languageContradictionDetector.CheckBatchAsync(requests);
                checkedStreams += outcomes.Count;

                var adopted = outcomes
                    .Where(outcome => outcome.Persisted)
                    .Where(outcome => outcome.Verdict == SubtitleLanguageVerdict.Contradiction)
                    .OrderByDescending(outcome => outcome.Confidence)
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
            "{ContradictionCount} contradictions adopted, {RequeuedCount} translation request(s) queued",
            candidates.Count,
            checkedStreams,
            contradictions,
            requeued);
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

        var halfLimit = Math.Max(maxItems / 2, 1);

        var movieRows = await _dbContext.Movies
            .Include(movie => movie.EmbeddedSubtitles)
            .Where(movie => !movie.ExcludeFromTranslation)
            .Where(movie => wantedStates.Contains(movie.TranslationState))
            .Where(movie => movie.EmbeddedSubtitles.Any(subtitle => subtitle.IsTextBased))
            .OrderBy(movie => movie.LastSubtitleCheckAt)
            .ThenBy(movie => movie.Id)
            .Take(halfLimit)
            .ToListAsync();

        var episodeRows = await _dbContext.Episodes
            .Include(episode => episode.EmbeddedSubtitles)
            .Where(episode => !episode.ExcludeFromTranslation)
            .Where(episode => wantedStates.Contains(episode.TranslationState))
            .Where(episode => episode.EmbeddedSubtitles.Any(subtitle => subtitle.IsTextBased))
            .OrderBy(episode => episode.LastSubtitleCheckAt)
            .ThenBy(episode => episode.Id)
            .Take(maxItems - movieRows.Count)
            .ToListAsync();

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

    private sealed record LanguageCheckCandidate(
        int MediaId,
        MediaType MediaType,
        string? Path,
        string? FileName,
        List<EmbeddedSubtitle> EmbeddedSubtitles);
}
