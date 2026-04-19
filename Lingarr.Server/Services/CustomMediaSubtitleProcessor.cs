using System.Security.Cryptography;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class CustomMediaSubtitleProcessor : ICustomMediaSubtitleProcessor
{
    private readonly LingarrDbContext _dbContext;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ISettingService _settingService;
    private readonly ILogger<CustomMediaSubtitleProcessor> _logger;

    public CustomMediaSubtitleProcessor(
        LingarrDbContext dbContext,
        ITranslationRequestService translationRequestService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        ISettingService settingService,
        ILogger<CustomMediaSubtitleProcessor> logger)
    {
        _dbContext = dbContext;
        _translationRequestService = translationRequestService;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<int> ProcessCustomItemForceAsync(
        CustomMediaItem item,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false)
    {
        var trackedItem = await _dbContext.CustomMediaItems
            .Include(customMediaItem => customMediaItem.CustomSource)
            .FirstOrDefaultAsync(customMediaItem => customMediaItem.Id == item.Id);

        if (trackedItem == null || string.IsNullOrWhiteSpace(trackedItem.Path))
        {
            return 0;
        }

        var mediaDirectory = Path.GetDirectoryName(trackedItem.Path);
        if (string.IsNullOrWhiteSpace(mediaDirectory))
        {
            return 0;
        }

        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? string.Empty;

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            return 0;
        }

        var externalSubtitles = await LoadMatchingExternalSubtitlesAsync(mediaDirectory, trackedItem.FileName);
        var embeddedSubtitles = await _subtitleExtractionService.ProbeEmbeddedSubtitles(trackedItem.Path);
        var mediaHash = CreateHash(externalSubtitles, embeddedSubtitles, sourceLanguages, targetLanguages, ignoreCaptions);

        if (!forceProcess && !string.IsNullOrWhiteSpace(trackedItem.MediaHash) && trackedItem.MediaHash == mediaHash)
        {
            return 0;
        }

        var sourceSubtitle = SelectExternalSourceSubtitle(externalSubtitles, sourceLanguages, ignoreCaptions);
        var mediaType = trackedItem.ItemKind == CustomMediaItemKind.Movie ? MediaType.Movie : MediaType.Episode;
        string? selectedSourceLanguage = sourceSubtitle?.Language;
        string? selectedSourceFormat = sourceSubtitle?.Format;
        bool useEmbeddedFallback = false;

        if (sourceSubtitle == null)
        {
            var embeddedMatch = SubtitleLanguageHelper.FindBestMatch(
                embeddedSubtitles.Where(subtitle => subtitle.IsTextBased).ToList(),
                sourceLanguages.ToList());

            if (embeddedMatch.Subtitle == null)
            {
                trackedItem.MediaHash = mediaHash;
                await _dbContext.SaveChangesAsync();
                return 0;
            }

            selectedSourceLanguage = embeddedMatch.MatchedLanguage;
            selectedSourceFormat = embeddedMatch.Subtitle.CodecName;
            useEmbeddedFallback = true;
        }

        if (string.IsNullOrWhiteSpace(selectedSourceLanguage))
        {
            trackedItem.MediaHash = mediaHash;
            await _dbContext.SaveChangesAsync();
            return 0;
        }

        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            await _settingService.GetSetting(SettingKeys.Translation.SubtitleOutputMode));
        var requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(
            sourceSubtitle != null
                ? ResolveSubtitleFormat(sourceSubtitle)
                : MapEmbeddedSubtitleFormat(selectedSourceFormat),
            subtitleOutputMode);
        var requiredOutputFormatsKey = SubtitleOutputModeHelper.SerializeFormats(requiredOutputFormats);

        var existingOutputLanguages = BuildExistingOutputLanguages(externalSubtitles, embeddedSubtitles);
        var translationsQueued = 0;

        foreach (var targetLanguage in targetLanguages)
        {
            if (!forceTranslation &&
                existingOutputLanguages.TryGetValue(targetLanguage, out var existingFormats) &&
                requiredOutputFormats.All(existingFormats.Contains))
            {
                continue;
            }

            if (await HasActiveRequestAsync(
                    trackedItem.Id,
                    selectedSourceLanguage,
                    targetLanguage,
                    requiredOutputFormatsKey))
            {
                continue;
            }

            await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
            {
                MediaId = 0,
                MediaType = mediaType,
                WorkloadKind = TranslationWorkloadKind.CustomSource,
                CustomMediaItemId = trackedItem.Id,
                SubtitlePath = useEmbeddedFallback ? null : sourceSubtitle!.Path,
                SubtitleFormat = selectedSourceFormat,
                SourceLanguage = selectedSourceLanguage,
                TargetLanguage = targetLanguage
            }, forcePriority);

            translationsQueued++;
        }

        trackedItem.MediaHash = mediaHash;
        await _dbContext.SaveChangesAsync();
        return translationsQueued;
    }

    private async Task<List<Subtitles>> LoadMatchingExternalSubtitlesAsync(string mediaDirectory, string mediaFileName)
    {
        var allSubtitles = await _subtitleService.GetAllSubtitles(mediaDirectory);
        var mediaNameNoExtension = Path.GetFileNameWithoutExtension(mediaFileName);

        return allSubtitles
            .Where(subtitle =>
                subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase) ||
                subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase) ||
                subtitle.FileName.StartsWith(mediaNameNoExtension + ".", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Subtitles? SelectExternalSourceSubtitle(
        IReadOnlyCollection<Subtitles> subtitles,
        IReadOnlyCollection<string> sourceLanguages,
        string ignoreCaptions)
    {
        var matchedLanguage = subtitles
            .Select(subtitle => subtitle.Language)
            .FirstOrDefault(language => sourceLanguages.Contains(language));

        if (matchedLanguage == null)
        {
            return null;
        }

        if (ignoreCaptions == "true")
        {
            return subtitles.FirstOrDefault(subtitle =>
                       subtitle.Language == matchedLanguage && string.IsNullOrWhiteSpace(subtitle.Caption))
                   ?? subtitles.FirstOrDefault(subtitle => subtitle.Language == matchedLanguage);
        }

        return subtitles.FirstOrDefault(subtitle => subtitle.Language == matchedLanguage);
    }

    private static Dictionary<string, HashSet<string>> BuildExistingOutputLanguages(
        IReadOnlyCollection<Subtitles> externalSubtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var subtitle in externalSubtitles)
        {
            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                continue;
            }

            if (!result.TryGetValue(normalizedLanguage, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[normalizedLanguage] = formats;
            }

            formats.Add(ResolveSubtitleFormat(subtitle));
        }

        foreach (var subtitle in embeddedSubtitles.Where(subtitle => subtitle.IsTextBased && !string.IsNullOrWhiteSpace(subtitle.Language)))
        {
            var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage))
            {
                continue;
            }

            if (!result.TryGetValue(normalizedLanguage, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[normalizedLanguage] = formats;
            }

            formats.Add(MapEmbeddedSubtitleFormat(subtitle.CodecName));
        }

        return result;
    }

    private async Task<HashSet<string>> GetLanguagesSetting<T>(string settingName) where T : class, ILanguage
    {
        var languages = await _settingService.GetSettingAsJson<T>(settingName);
        return languages
            .Select(language => language.Code.ToLowerInvariant())
            .ToHashSet();
    }

    private async Task<bool> HasActiveRequestAsync(
        int customMediaItemId,
        string sourceLanguage,
        string targetLanguage,
        string requestedRequiredOutputFormats)
    {
        var workloadItemKey = $"custom:{customMediaItemId}";
        var activeCandidates = await _dbContext.TranslationRequests
            .Where(request =>
                (request.WorkloadItemKey == workloadItemKey ||
                 (request.WorkloadKind == TranslationWorkloadKind.CustomSource &&
                   request.CustomMediaItemId == customMediaItemId)) &&
                request.SourceLanguage == sourceLanguage &&
                request.TargetLanguage == targetLanguage &&
                request.IsActive == true)
            .Select(request => new
            {
                request.RequiredOutputFormats,
                request.SourceSubtitleFormat,
                request.SubtitleOutputMode
            })
            .ToListAsync();

        return activeCandidates.Any(request =>
            string.Equals(
                NormalizeRequiredOutputFormats(
                    request.RequiredOutputFormats,
                    request.SourceSubtitleFormat,
                    request.SubtitleOutputMode),
                requestedRequiredOutputFormats,
                StringComparison.Ordinal));
    }

    private static string NormalizeRequiredOutputFormats(
        string? requiredOutputFormats,
        string? sourceSubtitleFormat,
        string? subtitleOutputMode = null)
    {
        if (!string.IsNullOrWhiteSpace(requiredOutputFormats))
        {
            var normalized = SubtitleOutputModeHelper.SerializeFormats(
                SubtitleOutputModeHelper.DeserializeFormats(requiredOutputFormats));
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return SubtitleOutputModeHelper.SerializeFormats(
            SubtitleOutputModeHelper.GetRequiredOutputFormats(
                sourceSubtitleFormat,
                SubtitleOutputModeHelper.Parse(subtitleOutputMode)));
    }

    private static string CreateHash(
        IReadOnlyCollection<Subtitles> subtitles,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> sourceLanguages,
        IReadOnlyCollection<string> targetLanguages,
        string ignoreCaptions)
    {
        using var sha256 = SHA256.Create();
        var subtitlePaths = string.Join("|", subtitles.Select(subtitle => subtitle.Path).OrderBy(path => path));
        var embeddedTokens = string.Join("|", embeddedSubtitles
            .OrderBy(subtitle => subtitle.StreamIndex)
            .Select(subtitle =>
                $"{subtitle.StreamIndex}:{subtitle.Language?.ToLowerInvariant()}:{subtitle.CodecName}:{subtitle.IsTextBased}:{subtitle.IsDefault}:{subtitle.IsForced}"));
        var sourceLanguageKey = string.Join(",", sourceLanguages.OrderBy(language => language));
        var targetLanguageKey = string.Join(",", targetLanguages.OrderBy(language => language));
        var hashInput = $"{subtitlePaths}|{embeddedTokens}|{sourceLanguageKey}|{targetLanguageKey}|{ignoreCaptions}|custom-v1";
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
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
