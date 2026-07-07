using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        var settings = await _settingService.GetSettings([
            SettingKeys.Translation.UseSubtitleTagging,
            SettingKeys.Translation.RemoveLanguageTag,
            SettingKeys.Translation.SubtitleTag,
            SettingKeys.Translation.SubtitleTagShort,
            SettingKeys.Translation.StripSubtitleFormatting,
            SettingKeys.Translation.SubtitleOutputMode
        ]);

        var originalSubtitles = await _subtitleService.ReadSubtitles(sourcePath);
        var translatedSubtitles = BuildTranslatedSubtitles(
            originalSubtitles,
            checkpoint?.Translations ?? new Dictionary<int, string>(),
            edits,
            sourceTextPositions);

        var useSubtitleTagging =
            settings.TryGetValue(SettingKeys.Translation.UseSubtitleTagging, out var useTaggingValue) &&
            string.Equals(useTaggingValue, "true", StringComparison.OrdinalIgnoreCase);
        var removeLanguageTag =
            settings.TryGetValue(SettingKeys.Translation.RemoveLanguageTag, out var removeLanguageTagValue) &&
            string.Equals(removeLanguageTagValue, "true", StringComparison.OrdinalIgnoreCase);
        var subtitleTag = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTag) ?? string.Empty
            : string.Empty;
        var subtitleTagShort = useSubtitleTagging
            ? settings.GetValueOrDefault(SettingKeys.Translation.SubtitleTagShort) ?? string.Empty
            : string.Empty;

        var targetLanguage = removeLanguageTag ? string.Empty : dbRequest.TargetLanguage;
        var stripFormatting =
            settings.TryGetValue(SettingKeys.Translation.StripSubtitleFormatting, out var stripValue) &&
            string.Equals(stripValue, "true", StringComparison.OrdinalIgnoreCase);

        var sourceFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(sourcePath));
        var subtitleOutputMode = SubtitleOutputModeHelper.Parse(
            !string.IsNullOrWhiteSpace(dbRequest.SubtitleOutputMode)
                ? dbRequest.SubtitleOutputMode
                : settings.GetValueOrDefault(SettingKeys.Translation.SubtitleOutputMode));
        var requiredOutputFormats = SubtitleOutputModeHelper.DeserializeFormats(dbRequest.RequiredOutputFormats);
        if (requiredOutputFormats.Count == 0)
        {
            requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(
                sourceFormat,
                subtitleOutputMode);
        }

        var writesPreservedAssOutput =
            SubtitleOutputModeHelper.IsAssFormat(sourceFormat) &&
            requiredOutputFormats.Any(SubtitleOutputModeHelper.IsAssFormat);
        dbRequest.SourceSubtitleFormat = sourceFormat;
        dbRequest.SubtitleOutputMode = subtitleOutputMode.ToSettingValue();
        dbRequest.RequiredOutputFormats = SubtitleOutputModeHelper.SerializeFormats(requiredOutputFormats);
        var writtenOutput = await WriteSubtitleOutputsAsync(
            dbRequest,
            sourcePath,
            translatedSubtitles,
            requiredOutputFormats,
            targetLanguage,
            subtitleTag,
            subtitleTagShort,
            stripFormatting,
            writesPreservedAssOutput,
            cancellationToken);

        dbRequest.Status = TranslationStatus.Completed;
        dbRequest.CompletedAt = DateTime.UtcNow;
        dbRequest.SubtitleToTranslate = sourcePath;
        dbRequest.TranslatedSubtitle = writtenOutput.PrimaryPath;
        dbRequest.GeneratedOutputFormats = writtenOutput.GeneratedFormats;
        dbRequest.GeneratedSubtitlePaths = JsonSerializer.Serialize(writtenOutput.OutputPaths);
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
            OutputPath: writtenOutput.PrimaryPath);
    }

    private static List<SubtitleItem> BuildTranslatedSubtitles(
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

    private async Task<WrittenSubtitleOutput> WriteSubtitleOutputsAsync(
        TranslationRequest request,
        string sourcePath,
        List<SubtitleItem> translatedSubtitles,
        IReadOnlyList<string> requiredOutputFormats,
        string targetLanguage,
        string subtitleTag,
        string subtitleTagShort,
        bool stripFormatting,
        bool writesPreservedAssOutput,
        CancellationToken cancellationToken)
    {
        var writtenOutputs = new List<(string Format, string Path)>();
        var outputCaption = SubtitleLanguageHelper.GetSupplementalOutputCaption(request.SourceSubtitleType);

        foreach (var outputFormat in requiredOutputFormats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var renderSubtitles = RenderOutputSubtitles(
                translatedSubtitles,
                outputFormat,
                writesPreservedAssOutput);
            var outputStripFormatting =
                stripFormatting &&
                !(writesPreservedAssOutput && SubtitleOutputModeHelper.IsAssFormat(outputFormat));
            var candidatePaths = _subtitleService.CreateFallbackPaths(
                    sourcePath,
                    targetLanguage,
                    subtitleTag,
                    subtitleTagShort,
                    outputFormat,
                    outputCaption)
                .Where(path => !IsSamePath(path, sourcePath))
                .ToList();

            Exception? lastException = null;
            foreach (var candidatePath in candidatePaths)
            {
                try
                {
                    EnsureParentDirectory(candidatePath);
                    await _subtitleService.WriteSubtitles(
                        candidatePath,
                        renderSubtitles,
                        outputStripFormatting);
                    writtenOutputs.Add((outputFormat, candidatePath));
                    lastException = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(
                        ex,
                        "Failed to write completed failed-translation output {Path} for request {RequestId}. Trying fallback path.",
                        candidatePath,
                        request.Id);
                }
            }

            if (lastException != null)
            {
                throw lastException;
            }

            if (!writtenOutputs.Any(output =>
                    string.Equals(output.Format, outputFormat, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Failed to write subtitle output for format {outputFormat}.");
            }
        }

        var primaryPath = writtenOutputs
            .OrderByDescending(output =>
                string.Equals(
                    SubtitleOutputModeHelper.NormalizeFormat(output.Format),
                    SubtitleOutputModeHelper.NormalizeFormat(request.SourceSubtitleFormat),
                    StringComparison.OrdinalIgnoreCase))
            .Select(output => output.Path)
            .First();
        var generatedFormats = SubtitleOutputModeHelper.SerializeFormats(
            writtenOutputs.Select(output => output.Format));

        _logger.LogInformation(
            "Completed failed translation request {RequestId} and created subtitle outputs: {SubtitleOutputs}",
            request.Id,
            string.Join(", ", writtenOutputs.Select(output => output.Path)));

        return new WrittenSubtitleOutput(
            primaryPath,
            generatedFormats,
            writtenOutputs.Select(output => output.Path).ToList());
    }

    private static List<SubtitleItem> RenderOutputSubtitles(
        List<SubtitleItem> translatedSubtitles,
        string outputFormat,
        bool writesPreservedAssOutput)
    {
        if (writesPreservedAssOutput && SubtitleOutputModeHelper.IsAssFormat(outputFormat))
        {
            return translatedSubtitles;
        }

        var renderedSubtitles = new List<SubtitleItem>(translatedSubtitles.Count);
        foreach (var subtitle in translatedSubtitles)
        {
            var translatedText = subtitle.TranslatedLines.Count > 0
                ? string.Join("\\N", subtitle.TranslatedLines)
                : string.Join("\\N", subtitle.Lines);
            var plainTextLines = PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(translatedText);

            if (PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(plainTextLines))
            {
                continue;
            }

            renderedSubtitles.Add(new SubtitleItem
            {
                Position = subtitle.Position,
                StartTime = subtitle.StartTime,
                EndTime = subtitle.EndTime,
                Lines = [.. subtitle.Lines],
                PlaintextLines = [.. subtitle.PlaintextLines],
                TranslatedLines = plainTextLines,
                SsaDialogue = subtitle.SsaDialogue,
                SsaFormat = subtitle.SsaFormat
            });
        }

        return renderedSubtitles;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool IsSamePath(string path, string? otherPath)
    {
        return !string.IsNullOrWhiteSpace(otherPath) &&
               string.Equals(
                   Path.GetFullPath(path),
                   Path.GetFullPath(otherPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record WrittenSubtitleOutput(
        string PrimaryPath,
        string GeneratedFormats,
        IReadOnlyCollection<string> OutputPaths);

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
