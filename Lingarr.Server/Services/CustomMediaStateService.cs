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

namespace Lingarr.Server.Services;

public class CustomMediaStateService : ICustomMediaStateService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly ILogger<CustomMediaStateService> _logger;

    public CustomMediaStateService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        ILogger<CustomMediaStateService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
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

        var hasExternalSource = externalSubtitles
            .Where(subtitle => !ShouldSkipExternalSourceCandidate(subtitle))
            .Any(subtitle =>
                sourceLanguages.Any(sourceLanguage =>
                    SubtitleLanguageHelper.LanguageMatches(subtitle.Language, sourceLanguage)));
        var hasEmbeddedSource = embeddedSubtitles.Any(subtitle =>
            subtitle.IsTextBased &&
            !string.IsNullOrWhiteSpace(subtitle.Language) &&
            sourceLanguages.Any(sourceLanguage => SubtitleLanguageHelper.LanguageMatches(subtitle.Language, sourceLanguage)));

        if (!hasExternalSource && !hasEmbeddedSource)
        {
            return TranslationState.AwaitingSource;
        }

        var requiredOutputFormats = ResolveRequiredOutputFormats(
            externalSubtitles,
            embeddedSubtitles,
            sourceLanguages,
            ignoreCaptions,
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

        var missingTargets = targetLanguages
            .Where(targetLanguage =>
                !existingTargetFormats.TryGetValue(targetLanguage, out var formats) ||
                requiredOutputFormats.Any(requiredFormat => !formats.Contains(requiredFormat)))
            .ToList();

        if (missingTargets.Count == 0)
        {
            var staleTargets = await GetStaleTargetLanguagesAsync(
                item.Id,
                targetLanguages,
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

    private async Task<HashSet<string>> GetConfiguredLanguages(string settingKey)
    {
        try
        {
            var languages = await _settingService.GetSettingAsJson<SourceLanguage>(settingKey);
            return languages
                .Select(language => language.Code.ToLowerInvariant())
                .ToHashSet();
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
            (request.Status == TranslationStatus.Pending || request.Status == TranslationStatus.InProgress));
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
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        bool ignoreCaptions,
        SubtitleOutputMode subtitleOutputMode)
    {
        var externalSource = externalSubtitles
            .Where(subtitle => !ShouldSkipExternalSourceCandidate(subtitle))
            .Where(subtitle => sourceLanguages.Any(sourceLanguage =>
                SubtitleLanguageHelper.LanguageMatches(subtitle.Language, sourceLanguage)))
            .OrderBy(subtitle => ignoreCaptions && !string.IsNullOrWhiteSpace(subtitle.Caption))
            .FirstOrDefault();

        if (externalSource != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                ResolveSubtitleFormat(externalSource),
                subtitleOutputMode);
        }

        var embeddedSourceCandidates = embeddedSubtitles
            .Where(subtitle => subtitle.IsTextBased)
            .ToList();
        var bestEmbeddedMatch = SubtitleLanguageHelper.FindBestMatch(
            embeddedSourceCandidates,
            sourceLanguages.ToList());

        if (bestEmbeddedMatch.Subtitle != null)
        {
            return SubtitleOutputModeHelper.GetRequiredOutputFormats(
                MapEmbeddedSubtitleFormat(bestEmbeddedMatch.Subtitle.CodecName),
                subtitleOutputMode);
        }

        return SubtitleOutputModeHelper.GetRequiredOutputFormats(".srt", subtitleOutputMode);
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

    private static bool ShouldSkipExternalSourceCandidate(Subtitles candidate)
    {
        var fileName = PathStringHelper.GetFileName(candidate.Path);
        if (fileName.StartsWith("lingarr_temp_source_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return SubtitleExtractionService.IsLingarrExtracted(candidate.Path)
                && SubtitleExtractionService.IsSparseSubtitle(candidate.Path);
        }
        catch
        {
            return false;
        }
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
