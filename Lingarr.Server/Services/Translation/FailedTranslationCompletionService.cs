using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Translation;

public class FailedTranslationCompletionService : IFailedTranslationCompletionService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationCheckpointService _checkpointService;
    private readonly ISettingService _settingService;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly IProgressService _progressService;
    private readonly IMediaStateService _mediaStateService;
    private readonly ILogger<FailedTranslationCompletionService> _logger;

    public FailedTranslationCompletionService(
        LingarrDbContext dbContext,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleService subtitleService,
        ITranslationCheckpointService checkpointService,
        ISettingService settingService,
        ITranslationRequestService translationRequestService,
        IProgressService progressService,
        IMediaStateService mediaStateService,
        ILogger<FailedTranslationCompletionService> logger)
    {
        _dbContext = dbContext;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _subtitleService = subtitleService;
        _checkpointService = checkpointService;
        _settingService = settingService;
        _translationRequestService = translationRequestService;
        _progressService = progressService;
        _mediaStateService = mediaStateService;
        _logger = logger;
    }

    public async Task<FailedTranslationCompletionResult> CompleteAsync(
        TranslationRequest request,
        IReadOnlyDictionary<int, string> edits,
        IReadOnlySet<int> sourceTextPositions,
        string logMessage,
        CancellationToken cancellationToken)
    {
        var dbRequest = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (dbRequest == null)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: $"Translation request {request.Id} was not found.");
        }

        if (dbRequest.Status == TranslationStatus.Completed &&
            !string.IsNullOrWhiteSpace(dbRequest.TranslatedSubtitle))
        {
            return new FailedTranslationCompletionResult(
                Completed: true,
                AlreadyCompleted: true,
                OutputPath: dbRequest.TranslatedSubtitle);
        }

        var sourcePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
            dbRequest,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "Source subtitle file could not be resolved.");
        }

        var checkpoint = await _checkpointService.LoadByRequestIdAsync(
            dbRequest.Id,
            cancellationToken);
        if ((checkpoint == null || checkpoint.Translations.Count == 0) &&
            sourceTextPositions.Count == 0 &&
            edits.Count == 0)
        {
            return new FailedTranslationCompletionResult(
                Completed: false,
                AlreadyCompleted: false,
                OutputPath: null,
                SkippedReason: "No checkpoint translations or source-preserved positions were available.");
        }

        var originalSubtitles = await _subtitleService.ReadSubtitles(sourcePath);
        var outputSubtitles = BuildOutputSubtitles(
            originalSubtitles,
            checkpoint?.Translations ?? new Dictionary<int, string>(),
            edits,
            sourceTextPositions);

        var settings = await _settingService.GetSettings([
            SettingKeys.Translation.UseSubtitleTagging,
            SettingKeys.Translation.RemoveLanguageTag,
            SettingKeys.Translation.SubtitleTag,
            SettingKeys.Translation.SubtitleTagShort,
            SettingKeys.Translation.StripSubtitleFormatting
        ]);

        var useSubtitleTagging =
            settings.TryGetValue(SettingKeys.Translation.UseSubtitleTagging, out var useTaggingValue) &&
            string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
        var removeLanguageTag =
            settings.TryGetValue(SettingKeys.Translation.RemoveLanguageTag, out var removeLanguageTagValue) &&
            string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
        var subtitleTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
            : string.Empty;

        var targetLanguage = removeLanguageTag ? string.Empty : dbRequest.TargetLanguage;
        var outputPath = _subtitleService.CreateFilePath(
            dbRequest.SubtitleToTranslate ?? sourcePath,
            targetLanguage,
            subtitleTag);

        var stripFormatting =
            settings.TryGetValue(SettingKeys.Translation.StripSubtitleFormatting, out var stripValue) &&
            string.Equals(stripValue, "true", StringComparison.OrdinalIgnoreCase);

        await _subtitleService.WriteSubtitles(outputPath, outputSubtitles, stripFormatting);

        dbRequest.Status = TranslationStatus.Completed;
        dbRequest.CompletedAt = DateTime.UtcNow;
        dbRequest.TranslatedSubtitle = outputPath;
        dbRequest.IsActive = null;
        dbRequest.NextRetryAt = null;
        dbRequest.PausedAt = null;
        dbRequest.PauseReason = null;
        dbRequest.PausedProvider = null;
        dbRequest.RetryCount = 0;

        _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = dbRequest.Id,
            Level = "Information",
            Message = logMessage
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _checkpointService.DeleteAsync(dbRequest.Id, cancellationToken);
        await NotifyCompletionAsync(dbRequest, cancellationToken);

        return new FailedTranslationCompletionResult(
            Completed: true,
            AlreadyCompleted: false,
            OutputPath: outputPath);
    }

    private static List<SubtitleItem> BuildOutputSubtitles(
        IReadOnlyList<SubtitleItem> originalSubtitles,
        IReadOnlyDictionary<int, string> checkpointTranslations,
        IReadOnlyDictionary<int, string> edits,
        IReadOnlySet<int> sourceTextPositions)
    {
        var outputSubtitles = new List<SubtitleItem>();
        foreach (var original in originalSubtitles)
        {
            var item = new SubtitleItem
            {
                Position = original.Position,
                StartTime = original.StartTime,
                EndTime = original.EndTime,
                Lines = [.. original.Lines],
                PlaintextLines = [.. original.PlaintextLines],
                TranslatedLines = [.. original.TranslatedLines],
                SsaDialogue = original.SsaDialogue,
                SsaFormat = original.SsaFormat
            };

            if (edits.TryGetValue(original.Position, out var editText))
            {
                item.TranslatedLines = [editText];
            }
            else if (checkpointTranslations.TryGetValue(original.Position, out var translated))
            {
                item.TranslatedLines = [translated];
            }
            else if (sourceTextPositions.Contains(original.Position))
            {
                item.TranslatedLines = [.. original.Lines];
            }

            outputSubtitles.Add(item);
        }

        return outputSubtitles;
    }

    private async Task NotifyCompletionAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(request, 100);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit completion updates for request {RequestId}",
                request.Id);
        }

        if (request.WorkloadKind != TranslationWorkloadKind.Library || !request.MediaId.HasValue)
        {
            return;
        }

        try
        {
            if (request.MediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies.FindAsync([request.MediaId.Value], cancellationToken);
                if (movie != null)
                {
                    await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
                }

                return;
            }

            var episode = await _dbContext.Episodes
                .Include(item => item.Season)
                .ThenInclude(item => item.Show)
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (episode != null)
            {
                await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to update media state after completing request {RequestId}",
                request.Id);
        }
    }
}
