using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lingarr.Server.Services;

public class CustomMediaStateService : ICustomMediaStateService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ILogger<CustomMediaStateService> _logger;

    public CustomMediaStateService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        ILogger<CustomMediaStateService> logger,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _logger = logger;
    }

    public async Task<TranslationState> UpdateStateAsync(CustomMediaItem item, bool saveChanges = true)
    {
        var trackedItem = await _dbContext.CustomMediaItems
            .Include(customMediaItem => customMediaItem.CustomSource)
            .FirstOrDefaultAsync(customMediaItem => customMediaItem.Id == item.Id);

        if (trackedItem == null)
        {
            return TranslationState.Unknown;
        }

        var currentVersion = await GetSettingsVersionAsync();
        var state = await ComputeStateAsync(trackedItem);

        trackedItem.TranslationState = state;
        trackedItem.StateSettingsVersion = currentVersion;

        if (saveChanges)
        {
            await _dbContext.SaveChangesAsync();
        }

        return state;
    }

    public async Task<List<CustomMediaItem>> GetItemsNeedingTranslationAsync(int limit, bool priorityFirst = true)
    {
        if (limit <= 0)
        {
            return [];
        }

        var currentVersion = await GetSettingsVersionAsync();
        var baseQuery = _dbContext.CustomMediaItems
            .Include(item => item.CustomSource)
            .Where(item => item.CustomSource.Enabled && item.CustomSource.IncludeInAutomation)
            .Where(item => !item.ExcludeFromTranslation);
        var actionableItemsQuery = baseQuery.Where(item => item.TranslationState == TranslationState.Pending
                                                        || item.TranslationState == TranslationState.Stale
                                                        || item.TranslationState == TranslationState.Unknown
                                                        || (item.StateSettingsVersion < currentVersion
                                                            && item.TranslationState != TranslationState.Complete)
                                                        || (item.TranslationState == TranslationState.AwaitingSource
                                                            && item.IndexedAt == null));
        var completedItemsQuery = baseQuery.Where(item => item.TranslationState == TranslationState.Complete);

        CustomMediaItem? reservedCompletedItem = null;
        if (limit > 1)
        {
            reservedCompletedItem = await ApplyAutomationOrdering(
                    completedItemsQuery,
                    priorityFirst)
                .FirstOrDefaultAsync();
        }

        var workItemBudget = limit - (reservedCompletedItem == null ? 0 : 1);
        var items = await ApplyAutomationOrdering(actionableItemsQuery, priorityFirst)
            .Take(workItemBudget)
            .ToListAsync();

        if (reservedCompletedItem != null)
        {
            items.Add(reservedCompletedItem);
        }

        if (items.Count >= limit)
        {
            return items;
        }

        var additionalCompletedItems = await ApplyAutomationOrdering(
                completedItemsQuery.Where(item => reservedCompletedItem == null || item.Id != reservedCompletedItem.Id),
                priorityFirst)
            .Take(limit - items.Count)
            .ToListAsync();

        items.AddRange(additionalCompletedItems);
        return items;
    }

    public async Task<int> GetSettingsVersionAsync()
    {
        var versionString = await _settingService.GetSetting(SettingKeys.Translation.LanguageSettingsVersion);
        return int.TryParse(versionString, out var version) ? version : 1;
    }

    public Task UpdateLastSubtitleCheckAt(int customMediaItemId)
    {
        return _dbContext.CustomMediaItems
            .Where(item => item.Id == customMediaItemId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastSubtitleCheckAt, DateTime.UtcNow));
    }

    private async Task<TranslationState> ComputeStateAsync(CustomMediaItem item)
    {
        if (item.ExcludeFromTranslation)
        {
            return TranslationState.NotApplicable;
        }

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

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            return TranslationState.NotApplicable;
        }

        if (await HasActiveTranslationRequestAsync(item.Id))
        {
            return TranslationState.InProgress;
        }

        if (await HasFailedTranslationRequestAsync(item.Id))
        {
            return TranslationState.Failed;
        }

        var mediaDirectory = PathStringHelper.GetDirectoryName(item.Path);
        var externalSubtitles = new List<Subtitles>();
        if (!string.IsNullOrWhiteSpace(mediaDirectory))
        {
            try
            {
                var allSubtitles = await _subtitleService.GetAllSubtitles(mediaDirectory);
                var mediaFileName = item.FileName;
                var mediaNameNoExtension = PathStringHelper.GetFileNameWithoutExtension(mediaFileName);
                externalSubtitles = allSubtitles
                    .Where(subtitle =>
                        subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase) ||
                        subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase) ||
                        subtitle.FileName.StartsWith(mediaNameNoExtension + ".", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to inspect external subtitles for custom item {ItemId}", item.Id);
            }
        }

        List<EmbeddedSubtitle> embeddedSubtitles = [];
        try
        {
            embeddedSubtitles = await _subtitleExtractionService.ProbeEmbeddedSubtitles(item.Path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to probe embedded subtitles for custom item {ItemId}", item.Id);
        }

        var externalSource = await _sourceSubtitleSnapshotService.ResolveExternalSourceAsync(
            item,
            externalSubtitles);
        externalSource ??= ResolveExternalSourceFallback(
            externalSubtitles,
            sourceLanguages,
            ignoreCaptions);
        var embeddedSourceSelection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            embeddedSubtitles.Where(subtitle => subtitle.IsTextBased).ToList(),
            sourceLanguages,
            allowCaptionFallback: !ignoreCaptions);
        var hasExternalSource = externalSource != null;
        var hasEmbeddedSource = embeddedSourceSelection.SelectedSubtitle != null;

        if (!hasExternalSource && !hasEmbeddedSource)
        {
            return TranslationState.AwaitingSource;
        }

        var requiredOutputFormats = ResolveRequiredOutputFormats(
            externalSource,
            embeddedSourceSelection,
            subtitleOutputMode);
        var sourceSnapshot = await _sourceSubtitleSnapshotService.ResolveCurrentSnapshotAsync(
            item,
            item.ItemKind == CustomMediaItemKind.Movie ? MediaType.Movie : MediaType.Episode,
            embeddedSubtitles,
            externalSubtitles);
        var existingTargetFormats = BuildExistingTargetFormats(
            externalSubtitles,
            embeddedSubtitles,
            targetLanguages,
            skipWhenTargetEmbedded);
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

        if (missingTargets.Count == 0)
        {
            var staleCheckTargetLanguages = targetLanguages
                .Where(targetLanguage => !EmbeddedTargetSubtitleHelper.IsSatisfiedTargetLanguage(
                    embeddedSatisfiedTargetLanguages,
                    targetLanguage))
                .ToList();
            var staleTargets = await GetStaleTargetLanguagesAsync(
                item.Id,
                staleCheckTargetLanguages,
                sourceSnapshot,
                requiredOutputFormats);
            if (staleTargets.Count > 0)
            {
                return TranslationState.Stale;
            }

            return TranslationState.Complete;
        }

        return TranslationState.Pending;
    }

    private async Task<List<string>> GetConfiguredLanguages(string settingKey)
    {
        try
        {
            var languages = await _settingService.GetSettingAsJson<SourceLanguage>(settingKey);
            return languages
                .Select(language => language.Code.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private Task<bool> HasActiveTranslationRequestAsync(int customMediaItemId)
    {
        return _dbContext.TranslationRequests.AnyAsync(request =>
            request.WorkloadKind == TranslationWorkloadKind.CustomSource &&
            request.CustomMediaItemId == customMediaItemId &&
            (request.Status == TranslationStatus.Pending ||
             request.Status == TranslationStatus.InProgress ||
             request.Status == TranslationStatus.Paused));
    }

    private Task<bool> HasFailedTranslationRequestAsync(int customMediaItemId)
    {
        return _dbContext.TranslationRequests.AnyAsync(request =>
            request.WorkloadKind == TranslationWorkloadKind.CustomSource &&
            request.CustomMediaItemId == customMediaItemId &&
            request.Status == TranslationStatus.Failed);
    }

    private async Task<HashSet<string>> GetStaleTargetLanguagesAsync(
        int customMediaItemId,
        IEnumerable<string> targetLanguages,
        SourceSubtitleSnapshot? currentSnapshot,
        IReadOnlyCollection<string> requiredOutputFormats)
    {
        var staleTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (currentSnapshot == null || requiredOutputFormats.Count == 0)
        {
            return staleTargets;
        }

        var normalizedTargets = targetLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedTargets.Count == 0)
        {
            return staleTargets;
        }

        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.CustomSource
                              && request.CustomMediaItemId == customMediaItemId
                              && request.Status == TranslationStatus.Completed)
            .OrderByDescending(request => request.CompletedAt)
            .ThenByDescending(request => request.Id)
            .ToListAsync();

        foreach (var normalizedTarget in normalizedTargets)
        {
            var remainingFormats = new HashSet<string>(requiredOutputFormats, StringComparer.OrdinalIgnoreCase);
            foreach (var request in requests)
            {
                var requestTarget = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);
                if (!string.Equals(requestTarget, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var coveredFormats = GetRequestCoveredFormats(request);
                coveredFormats.IntersectWith(remainingFormats);
                if (coveredFormats.Count == 0)
                {
                    continue;
                }

                if (_sourceSubtitleSnapshotService.IsRequestStaleForSnapshot(request, currentSnapshot))
                {
                    staleTargets.Add(normalizedTarget);
                    break;
                }

                remainingFormats.ExceptWith(coveredFormats);
                if (remainingFormats.Count == 0)
                {
                    break;
                }
            }
        }

        return staleTargets;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildExistingTargetFormats(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> targetLanguages,
        bool includeEmbeddedTargets)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var externalSubtitle in externalSubtitles)
        {
            if (ExternalSubtitleCandidateHelper.ShouldSkipAsMainTarget(externalSubtitle))
            {
                continue;
            }

            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(externalSubtitle.Language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                continue;
            }

            if (!result.TryGetValue(normalizedLanguage, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[normalizedLanguage] = formats;
            }

            formats.Add(ResolveSubtitleFormat(externalSubtitle));
        }

        if (!includeEmbeddedTargets)
        {
            return result;
        }

        foreach (var embeddedSubtitle in embeddedSubtitles)
        {
            if (!embeddedSubtitle.IsTextBased || string.IsNullOrWhiteSpace(embeddedSubtitle.Language))
            {
                continue;
            }

            foreach (var targetLanguage in targetLanguages)
            {
                if (!SubtitleLanguageHelper.LanguageMatches(embeddedSubtitle.Language, targetLanguage))
                {
                    continue;
                }

                if (SubtitleLanguageHelper.ScoreSubtitleCandidate(embeddedSubtitle, targetLanguage) < 30)
                {
                    break;
                }

                var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
                if (string.IsNullOrWhiteSpace(normalizedLanguage))
                {
                    break;
                }

                if (!result.TryGetValue(normalizedLanguage, out var formats))
                {
                    formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[normalizedLanguage] = formats;
                }

                formats.Add(MapEmbeddedSubtitleFormat(embeddedSubtitle.CodecName));
                break;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ResolveRequiredOutputFormats(
        ResolvedExternalSourceSubtitle? externalSource,
        SubtitleSourceSelectionResult embeddedSourceSelection,
        SubtitleOutputMode subtitleOutputMode)
    {
        if (externalSource?.Subtitle != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                ResolveSubtitleFormat(externalSource.Subtitle),
                subtitleOutputMode);
        }

        if (embeddedSourceSelection.SelectedSubtitle != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                MapEmbeddedSubtitleFormat(embeddedSourceSelection.SelectedSubtitle.CodecName),
                subtitleOutputMode);
        }

        return SubtitleOutputModeHelper.GetRequiredOutputFormats(".srt", subtitleOutputMode);
    }

    private ResolvedExternalSourceSubtitle? ResolveExternalSourceFallback(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyList<string> sourceLanguages,
        bool ignoreCaptions)
    {
        foreach (var sourceLanguage in sourceLanguages)
        {
            var matchingSubtitles = externalSubtitles
                .Where(subtitle => IsUsableExternalSourceCandidate(subtitle, sourceLanguage))
                .ToList();
            if (matchingSubtitles.Count == 0)
            {
                continue;
            }

            var selected = matchingSubtitles
                .FirstOrDefault(subtitle => string.IsNullOrWhiteSpace(subtitle.Caption));
            if (selected == null && ignoreCaptions)
            {
                continue;
            }

            selected ??= matchingSubtitles.First();

            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(sourceLanguage);
            var snapshot = _sourceSubtitleSnapshotService.CreateExternalSnapshot(
                selected.Path,
                normalizedLanguage) ?? new SourceSubtitleSnapshot
                {
                    Version = SourceSubtitleSnapshot.CurrentVersion,
                    SourceType = SourceSubtitleSnapshot.ExternalType,
                    SourceLanguage = normalizedLanguage,
                    Identity = $"external|{normalizedLanguage}|{selected.Path}",
                    Fingerprint = string.Empty,
                    SourcePath = selected.Path
                };

            return new ResolvedExternalSourceSubtitle
            {
                Subtitle = selected,
                SourceLanguage = normalizedLanguage,
                Snapshot = snapshot
            };
        }

        return null;
    }

    private static bool IsUsableExternalSourceCandidate(Subtitles candidate, string sourceLanguage)
    {
        if (!SubtitleLanguageHelper.LanguageMatches(candidate.Language, sourceLanguage))
        {
            return false;
        }

        var fileName = PathStringHelper.GetFileName(candidate.Path);
        if (fileName.StartsWith("lingarr_temp_source_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExternalSubtitleCandidateHelper.IsSupplementalOrCommentary(candidate))
        {
            return false;
        }

        if (ExternalSubtitleCandidateHelper.ShouldSkipAsPrimarySource(candidate))
        {
            return false;
        }

        return true;
    }

    private static string ResolveSubtitleFormat(Subtitles subtitle)
    {
        if (!string.IsNullOrWhiteSpace(subtitle.Format))
        {
            return SubtitleOutputModeHelper.NormalizeFormat(subtitle.Format);
        }

        var fromPath = Path.GetExtension(subtitle.Path);
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return SubtitleOutputModeHelper.NormalizeFormat(fromPath);
        }

        return SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(subtitle.FileName));
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

    private static HashSet<string> GetRequestCoveredFormats(TranslationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.GeneratedOutputFormats))
        {
            return SubtitleOutputModeHelper.DeserializeFormats(request.GeneratedOutputFormats)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredOutputFormats))
        {
            return SubtitleOutputModeHelper.DeserializeFormats(request.RequiredOutputFormats)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            return [SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(request.TranslatedSubtitle))];
        }

        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(
            request.SourceSubtitleFormat ?? Path.GetExtension(request.SubtitleToTranslate));
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(request.SubtitleOutputMode);
        return SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, subtitleOutputMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedQueryable<CustomMediaItem> ApplyAutomationOrdering(
        IQueryable<CustomMediaItem> query,
        bool priorityFirst)
    {
        if (priorityFirst)
        {
            return query
                .OrderByDescending(item => item.IsPriority)
                .ThenBy(item => item.PriorityDate)
                .ThenBy(item => item.LastSubtitleCheckAt)
                .ThenBy(item => item.DateAdded);
        }

        return query
            .OrderBy(item => item.LastSubtitleCheckAt)
            .ThenBy(item => item.DateAdded);
    }
}
