using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingarr.Server.Services;

/// <summary>
/// Manages translation state for media items.
/// Provides efficient querying for items needing translation work.
/// </summary>
public class MediaStateService : IMediaStateService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ITranslationQualityScorer? _qualityScorer;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ILogger<MediaStateService> _logger;

    public MediaStateService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ILogger<MediaStateService> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null,
        ITranslationQualityScorer? qualityScorer = null)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _qualityScorer = qualityScorer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranslationState> UpdateStateAsync(IMedia media, MediaType mediaType, bool saveChanges = true)
    {
        var currentVersion = await GetSettingsVersionAsync();
        
        // Get the actual entity for updating
        Movie? movie = null;
        Episode? episode = null;
        
        if (mediaType == MediaType.Movie)
        {
            movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == media.Id);
            if (movie == null) return TranslationState.Unknown;
        }
        else
        {
            episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .FirstOrDefaultAsync(e => e.Id == media.Id);
            if (episode == null) return TranslationState.Unknown;
        }
    
        var state = await ComputeStateAsync(
            movie as IMedia ?? episode!, 
            mediaType,
            movie?.EmbeddedSubtitles ?? episode!.EmbeddedSubtitles,
            movie?.ExcludeFromTranslation ?? episode!.ExcludeFromTranslation,
            episode?.Season?.ExcludeFromTranslation ?? false,
            episode?.Season?.Show?.ExcludeFromTranslation ?? false);
    
        // Update entity
        if (movie != null)
        {
            movie.TranslationState = state;
            movie.StateSettingsVersion = currentVersion;
        }
        else if (episode != null)
        {
            episode.TranslationState = state;
            episode.StateSettingsVersion = currentVersion;
        }
    
        if (saveChanges)
        {
            await _dbContext.SaveChangesAsync();
        }
        
        _logger.LogDebug(
            "Updated state for {Type} {Id} ({Title}): {State}",
            mediaType, media.Id, media.Title, state);

        return state;
    }

    private async Task<TranslationState> ComputeStateAsync(
        IMedia media,
        MediaType mediaType,
        List<EmbeddedSubtitle> embeddedSubtitles,
        bool mediaExcluded,
        bool seasonExcluded,
        bool showExcluded)
    {
        // 1. Check exclusions
        if (mediaExcluded || seasonExcluded || showExcluded)
        {
            return TranslationState.NotApplicable;
        }

        // 2. Get configured languages
        var sourceLanguages = await GetConfiguredLanguages(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetConfiguredLanguages(SettingKeys.Translation.TargetLanguages);
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            await _settingService.GetSetting(SettingKeys.Translation.SubtitleOutputMode));
        var ignoreCaptions = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var skipWhenTargetEmbedded = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded) ?? "true",
            "true",
            StringComparison.OrdinalIgnoreCase);
        var ocrEnabled = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled) ?? "true",
            "true",
            StringComparison.OrdinalIgnoreCase);

        // 3. Check auto mode first (when ON, configured source languages are ignored)
        var isAutoMode = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.SourceLanguageMode),
            "auto",
            StringComparison.OrdinalIgnoreCase);

        if ((!isAutoMode && sourceLanguages.Count == 0) || targetLanguages.Count == 0)
        {
            return TranslationState.NotApplicable;
        }

        // 4. Check for active translation request
        if (await HasActiveTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.InProgress;
        }

        // 5. Get external subtitles
        var externalSubtitles = new List<Subtitles>();
        var knownForcedDialogueGeneratedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(media.Path))
        {
            try
            {
                var allSubs = await _subtitleService.GetAllSubtitles(media.Path);
                var knownGeneratedPaths = await GetKnownGeneratedSubtitlePathsAsync(media.Id, mediaType);
                knownForcedDialogueGeneratedPaths =
                    await GetKnownForcedDialogueGeneratedSubtitlePathsAsync(media.Id, mediaType);
                externalSubtitles = MediaSubtitleMatcher.FilterMatchingSubtitles(
                    media.FileName,
                    allSubs,
                    knownGeneratedPaths);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get external subtitles for {Title}", media.Title);
            }
        }

        bool hasExternalSource, hasEmbeddedSource;

        // Resolve target format requirements before source check
        // so we can short-circuit if targets are already satisfied
        var formatSourceLanguages = isAutoMode ? [] : sourceLanguages;
        var requiredOutputFormats = await ResolveRequiredOutputFormatsAsync(
            externalSubtitles,
            embeddedSubtitles,
            formatSourceLanguages,
            ignoreCaptions,
            subtitleOutputMode);

        var existingTargetFormats = BuildExistingTargetFormats(
            externalSubtitles,
            embeddedSubtitles,
            targetLanguages,
            skipWhenTargetEmbedded,
            knownForcedDialogueGeneratedPaths);
        var embeddedSatisfiedTargetLanguages = skipWhenTargetEmbedded
            ? EmbeddedTargetSubtitleHelper.GetSatisfiedTargetLanguages(embeddedSubtitles, targetLanguages)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var missingTargets = targetLanguages
            .Where(targetLanguage => !EmbeddedTargetSubtitleHelper.IsSatisfiedTargetLanguage(
                embeddedSatisfiedTargetLanguages,
                targetLanguage))
            .Where(targetLanguage =>
                !existingTargetFormats.TryGetValue(targetLanguage, out var formats) ||
                requiredOutputFormats.Any(requiredFormat => !formats.Contains(requiredFormat)))
            .ToList();

        if (isAutoMode)
        {
            // Auto mode: use quality scorer to find best source from ALL streams
            var autoSource = await FindAutoSourceCandidateAsync(
                embeddedSubtitles, externalSubtitles, targetLanguages);
            if (autoSource != null)
            {
                _logger.LogInformation(
                    "Auto mode selected source language '{Language}' via quality scoring",
                    autoSource.Value.Language);
                hasEmbeddedSource = autoSource.Value.IsEmbedded;
                hasExternalSource = !autoSource.Value.IsEmbedded;
            }
            else
            {
                if (missingTargets.Count == 0)
                {
                    return TranslationState.Complete;
                }

                // Check if OCR is needed for image-based subtitles (any language)
                if (ocrEnabled && HasOcrBlockedSourceCandidate(embeddedSubtitles, [], ignoreCaptions))
                {
                    return TranslationState.OcrBlocked;
                }

                if (ocrEnabled && HasOcrPendingSourceCandidate(embeddedSubtitles, [], ignoreCaptions))
                {
                    return TranslationState.OcrPending;
                }

                return TranslationState.AwaitingSource;
            }
        }
        else
        {
            // Manual mode: use configured source languages
            var externalSourceSelection = ExternalSubtitleCandidateHelper.SelectPrimarySourceCandidate(
                externalSubtitles,
                sourceLanguages,
                ignoreCaptions);
            hasExternalSource = externalSourceSelection != null;
            var embeddedPrimarySelection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                embeddedSubtitles.Where(subtitle => subtitle.IsReadableSource()).ToList(),
                sourceLanguages.ToList(),
                allowCaptionFallback: !ignoreCaptions);
            hasEmbeddedSource = embeddedPrimarySelection.SelectedSubtitle != null;

            if (!hasExternalSource && !hasEmbeddedSource)
            {
                if (missingTargets.Count == 0)
                {
                    return TranslationState.Complete;
                }

                if (ocrEnabled && HasOcrBlockedSourceCandidate(embeddedSubtitles, sourceLanguages, ignoreCaptions))
                {
                    return TranslationState.OcrBlocked;
                }

                if (ocrEnabled && HasOcrPendingSourceCandidate(embeddedSubtitles, sourceLanguages, ignoreCaptions))
                {
                    return TranslationState.OcrPending;
                }

                return TranslationState.AwaitingSource;
            }
        }

        // 6. Check which targets are satisfied
        // In auto mode, use empty source languages for required format resolution (accept all)
        var sourceSnapshot = await _sourceSubtitleSnapshotService.ResolveCurrentSnapshotWithAutoAsync(
            media,
            mediaType,
            embeddedSubtitles,
            externalSubtitles,
            isAutoMode,
            targetLanguages);

        if (missingTargets.Count == 0)
        {
            var staleCheckTargetLanguages = targetLanguages
                .Where(targetLanguage => !EmbeddedTargetSubtitleHelper.IsSatisfiedTargetLanguage(
                    embeddedSatisfiedTargetLanguages,
                    targetLanguage))
                .ToList();
            var staleTargets = await _sourceSubtitleSnapshotService.GetStaleTargetLanguagesAsync(
                media.Id,
                mediaType,
                staleCheckTargetLanguages,
                sourceSnapshot);

            if (staleTargets.Count > 0)
            {
                _logger.LogDebug(
                    "Detected stale translated subtitles for {Type} {Id} ({Title}): {Targets}",
                    mediaType,
                    media.Id,
                    media.Title,
                    string.Join(", ", staleTargets));
                return TranslationState.Stale;
            }

            // Touch OCR cache files so they don't expire and trigger wasteful re-OCR
            foreach (var subtitle in embeddedSubtitles)
            {
                if (subtitle.HasUsableOcr())
                {
                    _embeddedSubtitleCacheService.Touch(subtitle.OcrExtractedPath!);
                }
            }

            return TranslationState.Complete;
        }

        // Re-check source availability: if the source was from stale DB records
        // that have since been cleaned up, AwaitingSource takes precedence over Failed
        if (!hasExternalSource && !hasEmbeddedSource)
        {
            return TranslationState.AwaitingSource;
        }

        if (await HasFailedTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.Failed;
        }

        // Has source, missing targets, no active or failed request = Pending
        return TranslationState.Pending;
    }

    /// <inheritdoc />
    public async Task MarkAllStaleAsync()
    {
        // Only mark items as stale if they are not already Complete or NotApplicable
        // Complete items will be re-validated on next access
        // This preserves progress for items that still satisfy requirements
        
        var movieCount = await _dbContext.Movies
            .Where(m => m.TranslationState != TranslationState.NotApplicable && 
                        m.TranslationState != TranslationState.Complete)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TranslationState, TranslationState.Stale));

        var episodeCount = await _dbContext.Episodes
            .Where(e => e.TranslationState != TranslationState.NotApplicable && 
                        e.TranslationState != TranslationState.Complete)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.TranslationState, TranslationState.Stale));

        _logger.LogInformation(
            "Marked {MovieCount} movies and {EpisodeCount} episodes as stale (Complete items preserved)",
            movieCount, episodeCount);
    }

    /// <inheritdoc />
    public async Task<List<(IMedia Media, MediaType Type)>> GetMediaNeedingTranslationAsync(int limit, bool priorityFirst = true)
    {
        var result = new List<(IMedia Media, MediaType Type)>();
        var halfLimit = Math.Max(limit / 2, 1);
        var currentVersion = await GetSettingsVersionAsync();

        // Query movies needing work
        var moviesQuery = _dbContext.Movies
            .Include(m => m.EmbeddedSubtitles)
            .Where(m => !m.ExcludeFromTranslation)
            .Where(m => m.TranslationState == TranslationState.Pending 
                     || m.TranslationState == TranslationState.Stale
                     || m.TranslationState == TranslationState.OcrPending
                     || m.TranslationState == TranslationState.Unknown
                     || m.StateSettingsVersion < currentVersion
                     || (m.TranslationState == TranslationState.AwaitingSource && m.IndexedAt == null));

        if (priorityFirst)
        {
            moviesQuery = moviesQuery
                .OrderByDescending(m => m.IsPriority)
                .ThenBy(m => m.PriorityDate)
                .ThenBy(m => m.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(m => m.DateAdded);
        }
        else
        {
            moviesQuery = moviesQuery
                .OrderBy(m => m.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(m => m.DateAdded);
        }

        var movies = await moviesQuery.Take(halfLimit).ToListAsync();
        result.AddRange(movies.Select(m => ((IMedia)m, MediaType.Movie)));

        // Query episodes needing work  
        var episodesQuery = _dbContext.Episodes
            .Include(e => e.EmbeddedSubtitles)
            .Include(e => e.Season)
            .ThenInclude(s => s.Show)
            .Where(e => !e.ExcludeFromTranslation)
            .Where(e => !e.Season.ExcludeFromTranslation)
            .Where(e => !e.Season.Show.ExcludeFromTranslation)
            .Where(e => e.TranslationState == TranslationState.Pending 
                     || e.TranslationState == TranslationState.Stale
                     || e.TranslationState == TranslationState.OcrPending
                     || e.TranslationState == TranslationState.Unknown
                     || e.StateSettingsVersion < currentVersion
                     || (e.TranslationState == TranslationState.AwaitingSource && e.IndexedAt == null));

        if (priorityFirst)
        {
            episodesQuery = episodesQuery
                .OrderByDescending(e => e.Season.Show.IsPriority)
                .ThenBy(e => e.Season.Show.PriorityDate)
                .ThenBy(e => e.LastSubtitleCheckAt) // Oldest check first (nulls first usually)
                .ThenBy(e => e.DateAdded);
        }
        else
        {
            episodesQuery = episodesQuery
                .OrderBy(e => e.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(e => e.DateAdded);
        }

        var episodes = await episodesQuery.Take(limit - movies.Count).ToListAsync();
        result.AddRange(episodes.Select(e => ((IMedia)e, MediaType.Episode)));

        return result;
    }

    /// <inheritdoc />
    public async Task<int> GetSettingsVersionAsync()
    {
        var versionStr = await _settingService.GetSetting(SettingKeys.Translation.LanguageSettingsVersion);
        return int.TryParse(versionStr, out var version) ? version : 1;
    }

    /// <inheritdoc />
    public async Task IncrementSettingsVersionAsync()
    {
        var current = await GetSettingsVersionAsync();
        var newVersion = current + 1;
        await _settingService.SetSetting(SettingKeys.Translation.LanguageSettingsVersion, newVersion.ToString());
        _logger.LogInformation("Incremented language settings version to {Version}", newVersion);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveTranslationRequestAsync(int mediaId, MediaType mediaType)
    {
        return await _dbContext.TranslationRequests.AnyAsync(tr =>
            tr.WorkloadKind == TranslationWorkloadKind.Library &&
            tr.MediaId == mediaId &&
            tr.MediaType == mediaType &&
            (tr.Status == TranslationStatus.Pending ||
             tr.Status == TranslationStatus.InProgress ||
             tr.Status == TranslationStatus.Paused));
    }

    /// <inheritdoc />
    public async Task<bool> HasFailedTranslationRequestAsync(int mediaId, MediaType mediaType)
    {
        return await _dbContext.TranslationRequests.AnyAsync(tr =>
            tr.WorkloadKind == TranslationWorkloadKind.Library &&
            tr.MediaId == mediaId &&
            tr.MediaType == mediaType &&
            tr.Status == TranslationStatus.Failed);
    }

    private async Task<List<string>> GetConfiguredLanguages(string settingKey)
    {
        try
        {
            var languages = await _settingService.GetSettingAsJson<SourceLanguage>(settingKey);
            return languages
                .Select(l => l.Code.ToLowerInvariant())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task UpdateLastSubtitleCheckAt(int mediaId, MediaType mediaType)
    {
        var now = DateTime.UtcNow;
        if (mediaType == MediaType.Movie)
        {
            await _dbContext.Movies
                .Where(m => m.Id == mediaId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastSubtitleCheckAt, now));
        }
        else
        {
            await _dbContext.Episodes
                .Where(e => e.Id == mediaId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastSubtitleCheckAt, now));
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRequiredOutputFormatsAsync(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        bool ignoreCaptions,
        SubtitleOutputMode subtitleOutputMode)
    {
        var externalSource = ExternalSubtitleCandidateHelper.SelectPrimarySourceCandidate(
            externalSubtitles,
            sourceLanguages,
            ignoreCaptions);

        if (externalSource != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                ResolveSubtitleFormat(externalSource.Subtitle),
                subtitleOutputMode);
        }

        var embeddedSourceCandidates = embeddedSubtitles
            .Where(subtitle => subtitle.IsReadableSource())
            .ToList();

        var sourceLanguageList = sourceLanguages.ToList();
        var embeddedSelection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            embeddedSourceCandidates,
            sourceLanguageList,
            allowCaptionFallback: !ignoreCaptions);
        if (embeddedSelection.SelectedSubtitle != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                MapEmbeddedSubtitleFormat(embeddedSelection.SelectedSubtitle.GetReadableSourceFormat()),
                subtitleOutputMode);
        }

        return SubtitleOutputModeHelper.GetRequiredOutputFormats(".srt", subtitleOutputMode);
    }

    private static string MapEmbeddedSubtitleFormat(string? codecName)
    {
        return SubtitleOutputModeHelper.NormalizeFormat(codecName) switch
        {
            ".ass" => ".ass",
            ".ssa" => ".ssa",
            ".vtt" or ".webvtt" => ".vtt",
            _ => ".srt"
        };
    }

    private static string ResolveSubtitleFormat(Subtitles subtitle)
    {
        if (!string.IsNullOrWhiteSpace(subtitle.Format))
        {
            return subtitle.Format;
        }

        var pathFormat = Path.GetExtension(subtitle.Path);
        if (!string.IsNullOrWhiteSpace(pathFormat))
        {
            return pathFormat;
        }

        return Path.GetExtension(subtitle.FileName);
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildExistingTargetFormats(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> targetLanguages,
        bool includeEmbeddedTargets,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths = null)
    {
        var existingTargetFormats = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var externalSubtitle in externalSubtitles)
        {
            if (ShouldSkipAsMainTarget(externalSubtitle, knownGeneratedPrimaryTargetPaths))
            {
                continue;
            }

            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(externalSubtitle.Language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                continue;
            }

            var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(
                ResolveSubtitleFormat(externalSubtitle));
            if (string.IsNullOrWhiteSpace(normalizedFormat))
            {
                continue;
            }

            if (!existingTargetFormats.TryGetValue(normalizedLanguage, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                existingTargetFormats[normalizedLanguage] = formats;
            }

            formats.Add(normalizedFormat);
        }

        if (!includeEmbeddedTargets)
        {
            return existingTargetFormats;
        }

        foreach (var embedded in embeddedSubtitles)
        {
            if (!embedded.IsTextBased || string.IsNullOrWhiteSpace(embedded.Language))
            {
                continue;
            }

            foreach (var targetLanguage in targetLanguages)
            {
                if (!SubtitleLanguageHelper.LanguageMatches(embedded.Language, targetLanguage))
                {
                    continue;
                }

                var isLingarrGeneratedTarget = IsLingarrGeneratedEmbeddedTarget(embedded);
                if (!isLingarrGeneratedTarget &&
                    SubtitleLanguageHelper.ScoreSubtitleCandidate(embedded, targetLanguage) < 30)
                {
                    break;
                }

                var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
                if (string.IsNullOrWhiteSpace(normalizedLanguage))
                {
                    break;
                }

                if (!existingTargetFormats.TryGetValue(normalizedLanguage, out var formats))
                {
                    formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    existingTargetFormats[normalizedLanguage] = formats;
                }

                formats.Add(MapEmbeddedSubtitleFormat(embedded.CodecName));
                break;
            }
        }

        return existingTargetFormats;
    }

    private static bool IsLingarrGeneratedEmbeddedTarget(EmbeddedSubtitle embedded)
    {
        return embedded.Title?.Contains("(Lingarr)", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ShouldSkipAsMainTarget(
        Subtitles subtitle,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths)
    {
        return ExternalSubtitleCandidateHelper.ShouldSkipAsMainTarget(subtitle) &&
               !IsKnownGeneratedPrimaryTarget(subtitle, knownGeneratedPrimaryTargetPaths);
    }

    private static bool IsKnownGeneratedPrimaryTarget(
        Subtitles subtitle,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths)
    {
        return !string.IsNullOrWhiteSpace(subtitle.Path) &&
               knownGeneratedPrimaryTargetPaths?.Contains(
                   MediaSubtitleMatcher.NormalizePath(subtitle.Path)) == true;
    }

    private async Task<HashSet<string>> GetKnownGeneratedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType)
    {
        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .Where(request => request.GeneratedSubtitlePaths != null && request.GeneratedSubtitlePaths != string.Empty)
            .ToListAsync();

        return MediaSubtitleMatcher.ExtractGeneratedPaths(requests);
    }

    private async Task<HashSet<string>> GetKnownForcedDialogueGeneratedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType)
    {
        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .Where(request => request.SourceSubtitleType == SubtitleLanguageHelper.TypeForcedDialogue)
            .Where(request =>
                (request.GeneratedSubtitlePaths != null && request.GeneratedSubtitlePaths != string.Empty) ||
                (request.TranslatedSubtitle != null && request.TranslatedSubtitle != string.Empty))
            .ToListAsync();

        var paths = MediaSubtitleMatcher.ExtractGeneratedPaths(requests)
            .Select(MediaSubtitleMatcher.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
            {
                paths.Add(MediaSubtitleMatcher.NormalizePath(request.TranslatedSubtitle));
            }
        }

        return paths;
    }

    private static bool HasOcrPendingSourceCandidate(
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        bool ignoreCaptions)
    {
        return GetOcrSourceCandidates(embeddedSubtitles, sourceLanguages, ignoreCaptions)
            .Any(subtitle => subtitle.OcrStatus is SubtitleOcrStatus.NotStarted
                or SubtitleOcrStatus.Queued
                or SubtitleOcrStatus.Processing
                || (subtitle.OcrStatus == SubtitleOcrStatus.Succeeded && !subtitle.HasUsableOcr()));
    }

    private static bool HasOcrBlockedSourceCandidate(
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        bool ignoreCaptions)
    {
        return GetOcrSourceCandidates(embeddedSubtitles, sourceLanguages, ignoreCaptions)
            .Any(subtitle => subtitle.OcrStatus is SubtitleOcrStatus.BlockedLowQuality
                or SubtitleOcrStatus.Failed);
    }

    private static IEnumerable<EmbeddedSubtitle> GetOcrSourceCandidates(
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        bool ignoreCaptions)
    {
        var candidates = embeddedSubtitles
            .Where(subtitle => !subtitle.IsTextBased)
            .Where(subtitle => IsSupportedOcrCodec(subtitle.CodecName))
            .Where(subtitle => !ignoreCaptions ||
                               !SubtitleLanguageHelper.IsCaptionSubtitleType(
                                   SubtitleLanguageHelper.DetermineSubtitleType(subtitle)))
            .Where(subtitle => !SubtitleLanguageHelper.IsSupplementalSubtitleType(
                SubtitleLanguageHelper.DetermineSubtitleType(subtitle)));

        if (sourceLanguages.Count == 0)
        {
            return candidates.OrderBy(subtitle => subtitle.StreamIndex);
        }

        // Tagged streams that match a configured source language take precedence.
        var languageMatched = candidates
            .Where(subtitle => sourceLanguages.Any(language =>
                SubtitleLanguageHelper.LanguageMatches(subtitle.Language, language)))
            .ToList();
        if (languageMatched.Count > 0)
        {
            return languageMatched
                .OrderByDescending(subtitle => sourceLanguages.Max(language =>
                    SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, language)))
                .ThenBy(subtitle => subtitle.StreamIndex);
        }

        // Untagged (empty/"und") bitmap streams are accepted as candidates too: a PGS track
        // without a language tag is often the sole full-dialogue track (e.g. an untagged
        // English PGS on a release), and the state machine's job here is to detect that OCR
        // is needed, not to prove the stream's language. Streams are ordered by index so the
        // lowest untagged stream is the effective candidate.
        return candidates
            .Where(IsUntaggedLanguage)
            .OrderBy(subtitle => subtitle.StreamIndex);
    }

    private static bool IsUntaggedLanguage(EmbeddedSubtitle subtitle)
    {
        return string.IsNullOrWhiteSpace(subtitle.Language) ||
               string.Equals(subtitle.Language, "und", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedOcrCodec(string? codecName)
    {
        return !string.IsNullOrWhiteSpace(codecName) &&
               (codecName.Equals("hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase) ||
                codecName.Equals("pgssub", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(string Language, bool IsEmbedded)?> FindAutoSourceCandidateAsync(
        List<EmbeddedSubtitle> embeddedSubtitles,
        List<Subtitles> externalSubtitles,
        IReadOnlyList<string> targetLanguages)
    {
        if (_qualityScorer == null)
        {
            _logger.LogWarning("Auto mode unavailable: TranslationQualityScorer is not registered");
            return null;
        }

        var scorer = _qualityScorer;
        var minAcceptable = scorer.MinimumAcceptableScore;

        // Score all candidates and pick the best one
        var bestCandidate = (Language: null as string, BestScore: 0.0, IsEmbedded: false);

        // Check embedded subtitles first (preferred)
        foreach (var subtitle in embeddedSubtitles.Where(s => s.IsReadableSource()))
        {
            if (string.IsNullOrWhiteSpace(subtitle.Language) ||
                subtitle.Language.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Compute aggregate score across all target languages
            var totalScore = 0.0;
            var scoredTargets = 0;
            foreach (var target in targetLanguages)
            {
                var score = scorer.ScoreDirection(subtitle.Language, target);
                if (score.HasValue)
                {
                    totalScore += score.Value;
                    scoredTargets++;
                }
            }

            if (scoredTargets == 0) continue;

            var avgScore = totalScore / scoredTargets;
            if (avgScore >= minAcceptable && avgScore > bestCandidate.BestScore)
            {
                bestCandidate = (subtitle.Language, avgScore, true);
                _logger.LogDebug(
                    "Auto mode: embedded stream {StreamIndex} ({Language}) average score {Score:F1} across {Count} targets",
                    subtitle.StreamIndex, subtitle.Language, avgScore, scoredTargets);
            }
        }

        // Check external subtitles
        foreach (var subtitle in externalSubtitles)
        {
            var language = SubtitleLanguageHelper.DetectLanguageFromFileName(subtitle.FileName);
            if (string.IsNullOrWhiteSpace(language) ||
                language.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var totalScore = 0.0;
            var scoredTargets = 0;
            foreach (var target in targetLanguages)
            {
                var score = scorer.ScoreDirection(language, target);
                if (score.HasValue)
                {
                    totalScore += score.Value;
                    scoredTargets++;
                }
            }

            if (scoredTargets == 0) continue;

            var avgScore = totalScore / scoredTargets;
            if (avgScore >= minAcceptable && avgScore > bestCandidate.BestScore)
            {
                bestCandidate = (language, avgScore, false);
                _logger.LogDebug(
                    "Auto mode: external subtitle '{FileName}' ({Language}) average score {Score:F1} across {Count} targets",
                    subtitle.FileName, language, avgScore, scoredTargets);
            }
        }

        if (bestCandidate.Language != null)
        {
            _logger.LogInformation(
                "Auto mode selected best source: {Language} ({Source}) with average score {Score:F1}",
                bestCandidate.Language, bestCandidate.IsEmbedded ? "embedded" : "external", bestCandidate.BestScore);
            return (bestCandidate.Language, bestCandidate.IsEmbedded);
        }

        return null;
    }
}
