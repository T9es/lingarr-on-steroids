using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleOutputReconciliationService : ISubtitleOutputReconciliationService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleOutputBackfillService _subtitleOutputBackfillService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ILogger<SubtitleOutputReconciliationService> _logger;

    public SubtitleOutputReconciliationService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleOutputBackfillService subtitleOutputBackfillService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ILogger<SubtitleOutputReconciliationService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleOutputBackfillService = subtitleOutputBackfillService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _logger = logger;
    }

    public async Task<SubtitleOutputReconciliationResponse> ReconcileLibraryOutputsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new SubtitleOutputReconciliationResponse();
        var (subtitleOutputMode, subtitleTag, subtitleTagShort, deletedPaths, skippedUnsafePaths) =
            await CreateReconciliationContextAsync();

        var movies = await _dbContext.Movies
            .Where(movie => !movie.ExcludeFromTranslation)
            .ToListAsync(cancellationToken);

        foreach (var movie in movies)
        {
            await ReconcileMediaAsync(
                movie,
                MediaType.Movie,
                subtitleOutputMode,
                subtitleTag,
                subtitleTagShort,
                deletedPaths,
                skippedUnsafePaths,
                response,
                cancellationToken);
        }

        var episodes = await _dbContext.Episodes
            .Include(episode => episode.Season)
            .ThenInclude(season => season.Show)
            .Where(episode => !episode.ExcludeFromTranslation)
            .Where(episode => !episode.Season.ExcludeFromTranslation)
            .Where(episode => !episode.Season.Show.ExcludeFromTranslation)
            .ToListAsync(cancellationToken);

        foreach (var episode in episodes)
        {
            await ReconcileMediaAsync(
                episode,
                MediaType.Episode,
                subtitleOutputMode,
                subtitleTag,
                subtitleTagShort,
                deletedPaths,
                skippedUnsafePaths,
                response,
                cancellationToken);
        }

        return response;
    }

    public async Task<SubtitleOutputReconciliationResponse> ReconcileMediaOutputsAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        var response = new SubtitleOutputReconciliationResponse();
        var (subtitleOutputMode, subtitleTag, subtitleTagShort, deletedPaths, skippedUnsafePaths) =
            await CreateReconciliationContextAsync();

        switch (mediaType)
        {
            case MediaType.Movie:
            {
                var movie = await _dbContext.Movies
                    .Where(item => item.Id == mediaId)
                    .Where(item => !item.ExcludeFromTranslation)
                    .FirstOrDefaultAsync(cancellationToken);
                if (movie != null)
                {
                    await ReconcileMediaAsync(
                        movie,
                        MediaType.Movie,
                        subtitleOutputMode,
                        subtitleTag,
                        subtitleTagShort,
                        deletedPaths,
                        skippedUnsafePaths,
                        response,
                        cancellationToken);
                }

                break;
            }
            case MediaType.Episode:
            {
                var episode = await _dbContext.Episodes
                    .Include(item => item.Season)
                    .ThenInclude(item => item.Show)
                    .Where(item => item.Id == mediaId)
                    .Where(item => !item.ExcludeFromTranslation)
                    .Where(item => !item.Season.ExcludeFromTranslation)
                    .Where(item => !item.Season.Show.ExcludeFromTranslation)
                    .FirstOrDefaultAsync(cancellationToken);
                if (episode != null)
                {
                    await ReconcileMediaAsync(
                        episode,
                        MediaType.Episode,
                        subtitleOutputMode,
                        subtitleTag,
                        subtitleTagShort,
                        deletedPaths,
                        skippedUnsafePaths,
                        response,
                        cancellationToken);
                }

                break;
            }
            default:
                break;
        }

        return response;
    }

    private async Task<(
        SubtitleOutputMode OutputMode,
        string SubtitleTag,
        string SubtitleTagShort,
        HashSet<string> DeletedPaths,
        HashSet<string> SkippedUnsafePaths)> CreateReconciliationContextAsync()
    {
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            await _settingService.GetSetting(SettingKeys.Translation.SubtitleOutputMode));
        var subtitleTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTag) ?? string.Empty;
        var subtitleTagShort = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty;
        return (
            subtitleOutputMode,
            subtitleTag,
            subtitleTagShort,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private async Task ReconcileMediaAsync(
        IMedia media,
        MediaType mediaType,
        SubtitleOutputMode subtitleOutputMode,
        string subtitleTag,
        string subtitleTagShort,
        HashSet<string> deletedPaths,
        HashSet<string> skippedUnsafePaths,
        SubtitleOutputReconciliationResponse response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        response.MediaItemsScanned++;

        if (string.IsNullOrWhiteSpace(media.Path) || string.IsNullOrWhiteSpace(media.FileName))
        {
            return;
        }

        try
        {
            var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
            var matchingSubtitles = FilterMatchingSubtitles(media.FileName, allSubtitles);
            await DeleteObsoleteManagedOutputsAsync(
                media,
                mediaType,
                matchingSubtitles,
                subtitleOutputMode,
                subtitleTag,
                subtitleTagShort,
                deletedPaths,
                skippedUnsafePaths,
                response,
                cancellationToken);

            var backfillResult = await _subtitleOutputBackfillService.BackfillMissingOutputsAsync(
                media,
                mediaType,
                matchingSubtitles,
                subtitleOutputMode,
                subtitleTag,
                subtitleTagShort,
                cancellationToken);
            response.BackfilledFiles += backfillResult.BackfilledFiles;
            response.BackfilledFromExternalSourceFiles += backfillResult.BackfilledFromExternalSourceFiles;
            response.BackfilledFromEmbeddedSourceFiles += backfillResult.BackfilledFromEmbeddedSourceFiles;
            response.BackfillSkippedFiles += backfillResult.BackfillSkippedFiles;
            response.SkippedUnsafeFiles += backfillResult.BackfillSkippedFiles;
            response.Errors.AddRange(backfillResult.Errors);

            var refreshedSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
            var refreshedMatchingSubtitles = FilterMatchingSubtitles(media.FileName, refreshedSubtitles);
            var shouldQueueRetranslation = backfillResult.RequiresRetranslation
                                          || await HasMissingManagedOutputsAsync(
                                              media.Id,
                                              mediaType,
                                              refreshedMatchingSubtitles,
                                              subtitleOutputMode,
                                              subtitleTag,
                                              subtitleTagShort,
                                              cancellationToken);
            if (!shouldQueueRetranslation)
            {
                return;
            }

            var hasActiveRequests = await HasActiveRequestsAsync(media.Id, mediaType, cancellationToken);
            var queued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
                media,
                mediaType,
                forceProcess: true,
                forceTranslation: false,
                forcePriority: true);

            response.QueuedTranslations += queued;
            response.QueuedForRetranslation += queued;
            if (queued == 0 && hasActiveRequests)
            {
                response.SkippedActiveRequests++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reconcile subtitle outputs for {MediaType} {MediaId}", mediaType, media.Id);
            response.Errors.Add($"{mediaType} {media.Id}: {ex.Message}");
        }
    }

    private async Task DeleteObsoleteManagedOutputsAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        SubtitleOutputMode subtitleOutputMode,
        string subtitleTag,
        string subtitleTagShort,
        HashSet<string> deletedPaths,
        HashSet<string> skippedUnsafePaths,
        SubtitleOutputReconciliationResponse response,
        CancellationToken cancellationToken)
    {
        var completedRequests = await _dbContext.TranslationRequests
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == media.Id && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .OrderByDescending(request => request.CompletedAt)
            .ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);

        foreach (var request in completedRequests)
        {
            var obsoleteFormats = GetObsoleteFormats(request, subtitleOutputMode);
            if (obsoleteFormats.Count == 0)
            {
                continue;
            }

            foreach (var path in GetKnownGeneratedPaths(request))
            {
                if (!obsoleteFormats.Contains(SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(path))))
                {
                    continue;
                }

                await DeleteKnownOutputAsync(path, request, deletedPaths, response, cancellationToken);
            }

            DeleteTaggedLegacyOutputs(
                request,
                matchingSubtitles,
                obsoleteFormats,
                subtitleTag,
                subtitleTagShort,
                deletedPaths,
                skippedUnsafePaths,
                response);
        }
    }

    private static HashSet<string> GetObsoleteFormats(
        TranslationRequest request,
        SubtitleOutputMode subtitleOutputMode)
    {
        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(
            !string.IsNullOrWhiteSpace(request.SourceSubtitleFormat)
                ? request.SourceSubtitleFormat
                : Path.GetExtension(request.SubtitleToTranslate));
        var desiredFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, subtitleOutputMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedFormats = GetRequestCoveredFormats(request);

        generatedFormats.ExceptWith(desiredFormats);
        return generatedFormats;
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

        return [];
    }

    private static List<string> GetKnownGeneratedPaths(TranslationRequest request)
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.GeneratedSubtitlePaths))
        {
            try
            {
                var generatedPaths = JsonSerializer.Deserialize<List<string>>(request.GeneratedSubtitlePaths);
                if (generatedPaths != null)
                {
                    paths.AddRange(generatedPaths);
                }
            }
            catch
            {
                paths.AddRange(request.GeneratedSubtitlePaths.Split(
                    '|',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            paths.Add(request.TranslatedSubtitle);
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task DeleteKnownOutputAsync(
        string path,
        TranslationRequest request,
        HashSet<string> deletedPaths,
        SubtitleOutputReconciliationResponse response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsSourcePath(path, request.SubtitleToTranslate) ||
            !File.Exists(path) ||
            !deletedPaths.Add(path))
        {
            return;
        }

        File.Delete(path);
        response.DeletedFiles++;
        await Task.CompletedTask;
    }

    private static void DeleteTaggedLegacyOutputs(
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        HashSet<string> obsoleteFormats,
        string subtitleTag,
        string subtitleTagShort,
        HashSet<string> deletedPaths,
        HashSet<string> skippedUnsafePaths,
        SubtitleOutputReconciliationResponse response)
    {
        var targetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return;
        }

        foreach (var subtitle in matchingSubtitles)
        {
            var subtitleLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language);
            if (!SubtitleLanguageHelper.LanguageMatches(subtitleLanguage, targetLanguage))
            {
                continue;
            }

            var format = SubtitleOutputModeHelper.NormalizeFormat(
                !string.IsNullOrWhiteSpace(subtitle.Format) ? subtitle.Format : Path.GetExtension(subtitle.Path));
            if (!obsoleteFormats.Contains(format) || IsSourcePath(subtitle.Path, request.SubtitleToTranslate))
            {
                continue;
            }

            if (!HasLingarrTag(subtitle.Path, subtitleTag, subtitleTagShort))
            {
                if (skippedUnsafePaths.Add(subtitle.Path))
                {
                    response.SkippedUnsafeFiles++;
                }
                continue;
            }

            if (!File.Exists(subtitle.Path) || !deletedPaths.Add(subtitle.Path))
            {
                continue;
            }

            File.Delete(subtitle.Path);
            response.DeletedFiles++;
        }
    }

    private async Task<bool> HasActiveRequestsAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        return await _dbContext.TranslationRequests
            .AnyAsync(
                request => request.WorkloadKind == TranslationWorkloadKind.Library
                           && request.MediaId == mediaId
                           && request.MediaType == mediaType
                           && request.IsActive == true,
                cancellationToken);
    }

    private async Task<bool> HasMissingManagedOutputsAsync(
        int mediaId,
        MediaType mediaType,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        SubtitleOutputMode subtitleOutputMode,
        string subtitleTag,
        string subtitleTagShort,
        CancellationToken cancellationToken)
    {
        var completedRequests = await _dbContext.TranslationRequests
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .ToListAsync(cancellationToken);

        if (completedRequests.Count == 0)
        {
            return true;
        }

        foreach (var request in completedRequests)
        {
            var targetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                continue;
            }

            var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(
                !string.IsNullOrWhiteSpace(request.SourceSubtitleFormat)
                    ? request.SourceSubtitleFormat
                    : Path.GetExtension(request.SubtitleToTranslate));
            var desiredFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, subtitleOutputMode);

            foreach (var desiredFormat in desiredFormats)
            {
                if (!HasManagedOutputForRequest(
                        request,
                        targetLanguage,
                        desiredFormat,
                        matchingSubtitles,
                        subtitleTag,
                        subtitleTagShort))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasManagedOutputForRequest(
        TranslationRequest request,
        string targetLanguage,
        string desiredFormat,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort)
    {
        var normalizedDesiredFormat = SubtitleOutputModeHelper.NormalizeFormat(desiredFormat);

        foreach (var path in GetKnownGeneratedPaths(request))
        {
            if (IsSourcePath(path, request.SubtitleToTranslate))
            {
                continue;
            }

            var pathFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(path));
            if (!string.Equals(pathFormat, normalizedDesiredFormat, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(path))
            {
                return true;
            }
        }

        return matchingSubtitles.Any(subtitle =>
        {
            if (IsSourcePath(subtitle.Path, request.SubtitleToTranslate))
            {
                return false;
            }

            var subtitleLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language);
            if (!SubtitleLanguageHelper.LanguageMatches(subtitleLanguage, targetLanguage))
            {
                return false;
            }

            var subtitleFormat = SubtitleOutputModeHelper.NormalizeFormat(
                !string.IsNullOrWhiteSpace(subtitle.Format) ? subtitle.Format : Path.GetExtension(subtitle.Path));
            if (!string.Equals(subtitleFormat, normalizedDesiredFormat, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(subtitle.Path, request.TranslatedSubtitle, StringComparison.OrdinalIgnoreCase)
                   || HasLingarrTag(subtitle.Path, subtitleTag, subtitleTagShort);
        });
    }

    private static List<Subtitles> FilterMatchingSubtitles(string mediaFileName, IEnumerable<Subtitles> subtitles)
    {
        var mediaNameNoExt = Path.GetFileNameWithoutExtension(mediaFileName);
        return subtitles
            .Where(subtitle =>
                subtitle.FileName.StartsWith(mediaFileName + ".", StringComparison.OrdinalIgnoreCase)
                || subtitle.FileName.Equals(mediaFileName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(mediaNameNoExt)
                    && subtitle.FileName.StartsWith(mediaNameNoExt + ".", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static bool HasLingarrTag(string path, string subtitleTag, string subtitleTagShort)
    {
        var fileName = Path.GetFileName(path);
        return (!string.IsNullOrWhiteSpace(subtitleTag)
                && fileName.Contains(subtitleTag, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(subtitleTagShort)
                   && fileName.Contains(subtitleTagShort, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourcePath(string path, string? sourcePath)
    {
        return !string.IsNullOrWhiteSpace(sourcePath) &&
               string.Equals(
                   Path.GetFullPath(path),
                   Path.GetFullPath(sourcePath),
                   StringComparison.OrdinalIgnoreCase);
    }
}
