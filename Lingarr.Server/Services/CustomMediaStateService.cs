using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class CustomMediaStateService : ICustomMediaStateService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ILogger<CustomMediaStateService> _logger;

    public CustomMediaStateService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ILogger<CustomMediaStateService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
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
        var currentVersion = await GetSettingsVersionAsync();
        var query = _dbContext.CustomMediaItems
            .Include(item => item.CustomSource)
            .Where(item => item.CustomSource.Enabled && item.CustomSource.IncludeInAutomation)
            .Where(item => !item.ExcludeFromTranslation)
            .Where(item => item.TranslationState == TranslationState.Pending
                        || item.TranslationState == TranslationState.Stale
                        || item.TranslationState == TranslationState.Unknown
                        || item.StateSettingsVersion < currentVersion
                        || (item.TranslationState == TranslationState.AwaitingSource && item.IndexedAt == null));

        if (priorityFirst)
        {
            query = query
                .OrderByDescending(item => item.IsPriority)
                .ThenBy(item => item.PriorityDate)
                .ThenBy(item => item.LastSubtitleCheckAt)
                .ThenBy(item => item.DateAdded);
        }
        else
        {
            query = query
                .OrderBy(item => item.LastSubtitleCheckAt)
                .ThenBy(item => item.DateAdded);
        }

        return await query.Take(limit).ToListAsync();
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

        var mediaDirectory = Path.GetDirectoryName(item.Path);
        var externalSubtitles = new List<Subtitles>();
        if (!string.IsNullOrWhiteSpace(mediaDirectory))
        {
            try
            {
                var allSubtitles = await _subtitleService.GetAllSubtitles(mediaDirectory);
                var mediaFileName = item.FileName;
                var mediaNameNoExtension = Path.GetFileNameWithoutExtension(mediaFileName);
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

        var hasExternalSource = externalSubtitles.Any(subtitle =>
            sourceLanguages.Any(sourceLanguage => SubtitleLanguageHelper.LanguageMatches(subtitle.Language, sourceLanguage)));
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
        var existingTargetFormats = BuildExistingTargetFormats(externalSubtitles, embeddedSubtitles);

        var missingTargets = targetLanguages
            .Where(targetLanguage =>
                !existingTargetFormats.TryGetValue(targetLanguage, out var formats) ||
                requiredOutputFormats.Any(requiredFormat => !formats.Contains(requiredFormat)))
            .ToList();

        if (missingTargets.Count == 0)
        {
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

    private static IReadOnlyDictionary<string, HashSet<string>> BuildExistingTargetFormats(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles)
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

        foreach (var embeddedSubtitle in embeddedSubtitles)
        {
            if (!embeddedSubtitle.IsTextBased || string.IsNullOrWhiteSpace(embeddedSubtitle.Language))
            {
                continue;
            }

            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(embeddedSubtitle.Language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                continue;
            }

            if (!result.TryGetValue(normalizedLanguage, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[normalizedLanguage] = formats;
            }

            formats.Add(MapEmbeddedSubtitleFormat(embeddedSubtitle.CodecName));
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
}
