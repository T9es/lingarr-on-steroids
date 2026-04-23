using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Extensions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleOutputBackfillService : ISubtitleOutputBackfillService
{
    private const int TimestampToleranceMs = 250;

    private static readonly Regex AssLeakPattern = new(
        @"\{\\[^}]*\}|\\[Nnh]",
        RegexOptions.Compiled);

    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly ILogger<SubtitleOutputBackfillService> _logger;

    public SubtitleOutputBackfillService(
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        ISubtitleExtractionService subtitleExtractionService,
        ILogger<SubtitleOutputBackfillService> logger)
    {
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _subtitleExtractionService = subtitleExtractionService;
        _logger = logger;
    }

    public async Task<SubtitleOutputBackfillResult> BackfillMissingOutputsAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        SubtitleOutputMode subtitleOutputMode,
        string subtitleTag,
        string subtitleTagShort,
        CancellationToken cancellationToken = default)
    {
        var result = new SubtitleOutputBackfillResult();
        if (subtitleOutputMode != SubtitleOutputMode.Both)
        {
            return result;
        }

        var completedRequests = await _dbContext.TranslationRequests
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == media.Id && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .OrderByDescending(request => request.CompletedAt)
            .ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);

        foreach (var request in completedRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SourceRequiresBothOutputs(request))
            {
                continue;
            }

            try
            {
                await BackfillRequestOutputsAsync(
                    media,
                    mediaType,
                    request,
                    matchingSubtitles,
                    subtitleTag,
                    subtitleTagShort,
                    result,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to locally backfill subtitle outputs for translation request {RequestId}",
                    request.Id);
                result.BackfillSkippedFiles++;
                result.RequiresRetranslation = true;
                result.Errors.Add($"Translation request {request.Id}: {ex.Message}");
            }
        }

        if (result.BackfilledFiles > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<SubtitleOutputBackfillResult> RepairExistingAssOutputsAsync(
        IMedia media,
        MediaType mediaType,
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort,
        CancellationToken cancellationToken = default)
    {
        var result = new SubtitleOutputBackfillResult();
        if (!SourceRequiresBothOutputs(request))
        {
            return result;
        }

        try
        {
            await RepairRequestOutputsAsync(
                media,
                mediaType,
                request,
                matchingSubtitles,
                subtitleTag,
                subtitleTagShort,
                result,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to locally repair subtitle outputs for translation request {RequestId}",
                request.Id);
            result.BackfillSkippedFiles++;
            result.RequiresRetranslation = true;
            result.Errors.Add($"Translation request {request.Id}: {ex.Message}");
        }

        if (result.RepairedFiles > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private async Task BackfillRequestOutputsAsync(
        IMedia media,
        MediaType mediaType,
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort,
        SubtitleOutputBackfillResult result,
        CancellationToken cancellationToken)
    {
        var sourceFormat = GetSourceFormat(request);
        var assPath = FindExistingOutputPath(request, matchingSubtitles, sourceFormat, subtitleTag, subtitleTagShort);
        var srtPath = FindExistingOutputPath(request, matchingSubtitles, ".srt", subtitleTag, subtitleTagShort);

        if (!string.IsNullOrWhiteSpace(assPath) && string.IsNullOrWhiteSpace(srtPath))
        {
            await TryBackfillSrtFromAssAsync(
                request,
                assPath,
                subtitleTag,
                subtitleTagShort,
                result,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(srtPath) && string.IsNullOrWhiteSpace(assPath))
        {
            await TryBackfillAssFromSrtAsync(
                media,
                mediaType,
                request,
                srtPath,
                sourceFormat,
                matchingSubtitles,
                subtitleTag,
                subtitleTagShort,
                result,
                cancellationToken);
        }
    }

    private async Task RepairRequestOutputsAsync(
        IMedia media,
        MediaType mediaType,
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort,
        SubtitleOutputBackfillResult result,
        CancellationToken cancellationToken)
    {
        var sourceFormat = GetSourceFormat(request);
        var assPath = FindExistingOutputPath(request, matchingSubtitles, sourceFormat, subtitleTag, subtitleTagShort);
        if (string.IsNullOrWhiteSpace(assPath) || !File.Exists(assPath))
        {
            MarkRequiresRetranslation(result);
            return;
        }

        var translatedAss = await _subtitleService.ReadSubtitles(assPath);
        var srtPath = FindExistingOutputPath(request, matchingSubtitles, ".srt", subtitleTag, subtitleTagShort);
        var assNeedsInlineRepair = TranslatedAssNeedsInlineRepair(translatedAss);
        var srtNeedsRepair = await ExistingSrtNeedsRepairAsync(srtPath, cancellationToken);

        if (!assNeedsInlineRepair && !srtNeedsRepair)
        {
            return;
        }

        if (!assNeedsInlineRepair)
        {
            await RewritePlainSrtFromTranslatedAssAsync(request, translatedAss, srtPath, result, cancellationToken);
            return;
        }

        var sourceResolution = await ResolveSourceAssForBackfillAsync(
            media,
            mediaType,
            request,
            matchingSubtitles,
            sourceFormat,
            cancellationToken);
        if (sourceResolution == null)
        {
            MarkRequiresRetranslation(result);
            return;
        }

        var sourceAss = await _subtitleService.ReadSubtitles(sourceResolution.Path);
        if (!TryAlignTranslatedCues(sourceAss, translatedAss, out var translatedBySourceIndex))
        {
            MarkRequiresRetranslation(result);
            return;
        }

        var assParser = new AssTextStructureParser();
        for (var index = 0; index < sourceAss.Count; index++)
        {
            var translatedCue = translatedBySourceIndex[index];
            var translatedText = BuildRepairVisibleText(translatedCue);
            var structure = new SubtitleTextStructure(
                SubtitleStructureMode.Ass,
                sourceAss[index].Lines,
                assParser.Parse(sourceAss[index].Lines));

            sourceAss[index].TranslatedLines = structure.ApplyProviderTranslationAsSingleVisibleText(translatedText);
        }

        SetSequentialPositions(sourceAss);
        await _subtitleService.WriteSubtitles(assPath, sourceAss, stripSubtitleFormatting: false);
        AddGeneratedOutput(request, assPath, sourceFormat, preferAsPrimary: true);

        if (TrySelectWritableOutputPath(
                request,
                sourceResolution.Path,
                ".srt",
                subtitleTag,
                subtitleTagShort,
                out var repairedSrtPath))
        {
            var plainSrtItems = BuildPlainTextSrtItems(sourceAss);
            if (plainSrtItems.Count > 0)
            {
                SetSequentialPositions(plainSrtItems);
                await _subtitleService.WriteSubtitles(repairedSrtPath, plainSrtItems, stripSubtitleFormatting: true);
                AddGeneratedOutput(request, repairedSrtPath, ".srt", preferAsPrimary: false);
            }
        }

        result.RepairedFiles++;
        if (sourceResolution.SourceKind == BackfillSourceKind.Embedded)
        {
            result.BackfilledFromEmbeddedSourceFiles++;
        }
        else
        {
            result.BackfilledFromExternalSourceFiles++;
        }
    }

    private async Task RewritePlainSrtFromTranslatedAssAsync(
        TranslationRequest request,
        IReadOnlyList<SubtitleItem> translatedAss,
        string? srtPath,
        SubtitleOutputBackfillResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(srtPath))
        {
            MarkSkipped(result);
            return;
        }

        var plainSrtItems = BuildPlainTextSrtItems(translatedAss);
        if (plainSrtItems.Count == 0)
        {
            MarkSkipped(result);
            return;
        }

        SetSequentialPositions(plainSrtItems);
        await _subtitleService.WriteSubtitles(srtPath, plainSrtItems, stripSubtitleFormatting: true);
        AddGeneratedOutput(request, srtPath, ".srt", preferAsPrimary: false);
        result.RepairedFiles++;
    }

    private async Task TryBackfillSrtFromAssAsync(
        TranslationRequest request,
        string translatedAssPath,
        string subtitleTag,
        string subtitleTagShort,
        SubtitleOutputBackfillResult result,
        CancellationToken cancellationToken)
    {
        if (!TrySelectWritableOutputPath(
                request,
                !string.IsNullOrWhiteSpace(request.SubtitleToTranslate)
                    ? request.SubtitleToTranslate
                    : translatedAssPath,
                ".srt",
                subtitleTag,
                subtitleTagShort,
                out var outputPath))
        {
            MarkSkipped(result);
            return;
        }

        var translatedAss = await _subtitleService.ReadSubtitles(translatedAssPath);
        if (translatedAss.Count == 0)
        {
            MarkSkipped(result);
            return;
        }

        var plainSrtItems = BuildPlainTextSrtItems(translatedAss);
        if (plainSrtItems.Count == 0)
        {
            MarkSkipped(result);
            return;
        }

        SetSequentialPositions(plainSrtItems);
        await _subtitleService.WriteSubtitles(outputPath, plainSrtItems, stripSubtitleFormatting: true);
        AddGeneratedOutput(request, outputPath, ".srt", preferAsPrimary: false);
        result.BackfilledFiles++;
    }

    private async Task TryBackfillAssFromSrtAsync(
        IMedia media,
        MediaType mediaType,
        TranslationRequest request,
        string translatedSrtPath,
        string sourceFormat,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string subtitleTag,
        string subtitleTagShort,
        SubtitleOutputBackfillResult result,
        CancellationToken cancellationToken)
    {
        var sourceResolution = await ResolveSourceAssForBackfillAsync(
            media,
            mediaType,
            request,
            matchingSubtitles,
            sourceFormat,
            cancellationToken);
        if (sourceResolution == null)
        {
            MarkRequiresRetranslation(result);
            return;
        }

        if (!TrySelectWritableOutputPath(
                request,
                sourceResolution.Path,
                sourceFormat,
                subtitleTag,
                subtitleTagShort,
                out var outputPath))
        {
            MarkSkipped(result);
            return;
        }

        var translatedSrt = await _subtitleService.ReadSubtitles(translatedSrtPath);
        if (PlainSubtitleLooksDamaged(translatedSrt))
        {
            MarkRequiresRetranslation(result);
            return;
        }

        var sourceAss = await _subtitleService.ReadSubtitles(sourceResolution.Path);
        if (!TryAlignTranslatedCues(sourceAss, translatedSrt, out var translatedBySourceIndex))
        {
            MarkRequiresRetranslation(result);
            return;
        }

        var assParser = new AssTextStructureParser();
        for (var index = 0; index < sourceAss.Count; index++)
        {
            var translatedCue = translatedBySourceIndex[index];
            var translatedText = string.Join('\n', translatedCue.Lines.Count > 0
                ? translatedCue.Lines
                : translatedCue.PlaintextLines);
            var structure = new SubtitleTextStructure(
                SubtitleStructureMode.Ass,
                sourceAss[index].Lines,
                assParser.Parse(sourceAss[index].Lines));

            sourceAss[index].TranslatedLines = structure.ApplyProviderTranslationAsSingleVisibleText(translatedText);
        }

        SetSequentialPositions(sourceAss);
        await _subtitleService.WriteSubtitles(outputPath, sourceAss, stripSubtitleFormatting: false);
        AddGeneratedOutput(request, outputPath, sourceFormat, preferAsPrimary: true);
        result.BackfilledFiles++;
        if (sourceResolution.SourceKind == BackfillSourceKind.Embedded)
        {
            result.BackfilledFromEmbeddedSourceFiles++;
        }
        else
        {
            result.BackfilledFromExternalSourceFiles++;
        }
    }

    private async Task<ResolvedBackfillSource?> ResolveSourceAssForBackfillAsync(
        IMedia media,
        MediaType mediaType,
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string sourceFormat,
        CancellationToken cancellationToken)
    {
        if (!SubtitleOutputModeHelper.IsAssFormat(sourceFormat))
        {
            return null;
        }

        if (TryResolveExistingSourceFile(request, out var existingSourcePath))
        {
            return new ResolvedBackfillSource(existingSourcePath, BackfillSourceKind.External);
        }

        if (!string.Equals(request.SourceSnapshotType, SourceSubtitleSnapshot.EmbeddedType, StringComparison.Ordinal))
        {
            return null;
        }

        var embeddedSubtitles = await LoadCurrentEmbeddedSubtitlesAsync(media, mediaType, cancellationToken);
        if (embeddedSubtitles.Count == 0)
        {
            return null;
        }

        var currentSnapshot = await _sourceSubtitleSnapshotService.ResolveCurrentSnapshotAsync(
            media,
            mediaType,
            embeddedSubtitles,
            matchingSubtitles,
            cancellationToken);
        if (currentSnapshot == null ||
            !string.Equals(currentSnapshot.SourceType, SourceSubtitleSnapshot.EmbeddedType, StringComparison.Ordinal) ||
            _sourceSubtitleSnapshotService.IsRequestStaleForSnapshot(request, currentSnapshot) ||
            !currentSnapshot.StreamIndex.HasValue)
        {
            return null;
        }

        var matchedEmbeddedSubtitle = embeddedSubtitles.FirstOrDefault(subtitle =>
            subtitle.StreamIndex == currentSnapshot.StreamIndex.Value &&
            subtitle.IsTextBased &&
            SubtitleOutputModeHelper.IsAssFormat(subtitle.CodecName));
        if (matchedEmbeddedSubtitle == null ||
            string.IsNullOrWhiteSpace(media.Path) ||
            string.IsNullOrWhiteSpace(media.FileName))
        {
            return null;
        }

        var extractedPath = await _subtitleExtractionService.ExtractSubtitle(
            Path.Combine(media.Path, media.FileName),
            matchedEmbeddedSubtitle.StreamIndex,
            media.Path,
            matchedEmbeddedSubtitle.CodecName,
            matchedEmbeddedSubtitle.Language);
        if (string.IsNullOrWhiteSpace(extractedPath) ||
            !File.Exists(extractedPath) ||
            !SubtitleOutputModeHelper.IsAssFormat(Path.GetExtension(extractedPath)))
        {
            return null;
        }

        request.SubtitleToTranslate = extractedPath;
        request.SourceSubtitleFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(extractedPath));
        _logger.LogInformation(
            "Resolved embedded source subtitle stream {StreamIndex} for local output backfill of request {RequestId}: {Path}",
            matchedEmbeddedSubtitle.StreamIndex,
            request.Id,
            extractedPath);
        return new ResolvedBackfillSource(extractedPath, BackfillSourceKind.Embedded);
    }

    private async Task<List<EmbeddedSubtitle>> LoadCurrentEmbeddedSubtitlesAsync(
        IMedia media,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        if (mediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(item => item.EmbeddedSubtitles)
                .FirstOrDefaultAsync(item => item.Id == media.Id, cancellationToken);
            if (movie == null)
            {
                return [];
            }

            if (movie.EmbeddedSubtitles.Count == 0)
            {
                await _subtitleExtractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            return movie.EmbeddedSubtitles.ToList();
        }

        if (mediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .Include(item => item.EmbeddedSubtitles)
                .FirstOrDefaultAsync(item => item.Id == media.Id, cancellationToken);
            if (episode == null)
            {
                return [];
            }

            if (episode.EmbeddedSubtitles.Count == 0)
            {
                await _subtitleExtractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(item => item.EmbeddedSubtitles).LoadAsync(cancellationToken);
            }

            return episode.EmbeddedSubtitles.ToList();
        }

        return [];
    }

    private static bool TryAlignTranslatedCues(
        IReadOnlyList<SubtitleItem> sourceAss,
        IReadOnlyList<SubtitleItem> translatedSrt,
        out Dictionary<int, SubtitleItem> translatedBySourceIndex)
    {
        translatedBySourceIndex = [];
        if (sourceAss.Count == 0 || translatedSrt.Count == 0)
        {
            return false;
        }

        if (sourceAss.Count == translatedSrt.Count)
        {
            for (var index = 0; index < sourceAss.Count; index++)
            {
                translatedBySourceIndex[index] = translatedSrt[index];
            }

            return true;
        }

        var usedSourceIndexes = new HashSet<int>();
        foreach (var translatedCue in translatedSrt)
        {
            var candidates = sourceAss
                .Select((sourceCue, index) => new { sourceCue, index })
                .Where(candidate => !usedSourceIndexes.Contains(candidate.index))
                .Where(candidate =>
                    Math.Abs(candidate.sourceCue.StartTime - translatedCue.StartTime) <= TimestampToleranceMs &&
                    Math.Abs(candidate.sourceCue.EndTime - translatedCue.EndTime) <= TimestampToleranceMs)
                .ToList();

            if (candidates.Count != 1)
            {
                translatedBySourceIndex = [];
                return false;
            }

            translatedBySourceIndex[candidates[0].index] = translatedCue;
            usedSourceIndexes.Add(candidates[0].index);
        }

        if (translatedBySourceIndex.Count != sourceAss.Count)
        {
            translatedBySourceIndex = [];
            return false;
        }

        return true;
    }

    private static bool PlainSubtitleLooksDamaged(IReadOnlyList<SubtitleItem> subtitles)
    {
        var lines = subtitles.SelectMany(item => item.Lines);
        var drawingScan = AssSubtitleArtifactDetector.DetectDrawingArtifacts(lines);
        return drawingScan.HasIssues || subtitles.Any(item => item.Lines.Any(line => AssLeakPattern.IsMatch(line)));
    }

    private static bool TranslatedAssNeedsInlineRepair(IReadOnlyList<SubtitleItem> subtitles)
    {
        return AssSubtitleArtifactDetector
            .DetectInlineTagPlacementArtifacts(subtitles.SelectMany(item => item.Lines))
            .HasIssues;
    }

    private static async Task<bool> ExistingSrtNeedsRepairAsync(
        string? srtPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(srtPath) || !File.Exists(srtPath))
        {
            return false;
        }

        var lines = await File.ReadAllLinesAsync(srtPath, cancellationToken);
        return AssSubtitleArtifactDetector.DetectDrawingArtifacts(lines).HasIssues;
    }

    private static string BuildRepairVisibleText(SubtitleItem translatedCue)
    {
        var lines = translatedCue.Lines.Count > 0
            ? translatedCue.Lines
            : translatedCue.PlaintextLines;

        return string.Join('\n', lines.Select(ConvertAssLineToRepairVisibleText));
    }

    private static string ConvertAssLineToRepairVisibleText(string line)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '{')
            {
                var endIndex = line.IndexOf('}', index + 1);
                if (endIndex < 0)
                {
                    break;
                }

                var previousVisible = LastVisibleCharacter(builder);
                var nextVisible = FindNextVisibleCharacter(line, endIndex + 1);
                if (char.IsLetterOrDigit(previousVisible) &&
                    char.IsLetterOrDigit(nextVisible) &&
                    (builder.Length == 0 || !char.IsWhiteSpace(builder[^1])))
                {
                    builder.Append(' ');
                }

                index = endIndex;
                continue;
            }

            if (current == '\\' && index + 1 < line.Length)
            {
                var escaped = line[index + 1];
                if (escaped == 'N' || escaped == 'n')
                {
                    builder.Append('\n');
                    index++;
                    continue;
                }

                if (escaped == 'h')
                {
                    builder.Append(' ');
                    index++;
                    continue;
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static char LastVisibleCharacter(StringBuilder builder)
    {
        for (var index = builder.Length - 1; index >= 0; index--)
        {
            if (!char.IsWhiteSpace(builder[index]))
            {
                return builder[index];
            }
        }

        return '\0';
    }

    private static char FindNextVisibleCharacter(string line, int startIndex)
    {
        for (var index = startIndex; index < line.Length; index++)
        {
            if (line[index] == '{')
            {
                var endIndex = line.IndexOf('}', index + 1);
                if (endIndex < 0)
                {
                    return '\0';
                }

                index = endIndex;
                continue;
            }

            if (line[index] == '\\' && index + 1 < line.Length)
            {
                var escaped = line[index + 1];
                if (escaped == 'N' || escaped == 'n' || escaped == 'h')
                {
                    return ' ';
                }
            }

            if (!char.IsWhiteSpace(line[index]))
            {
                return line[index];
            }
        }

        return '\0';
    }

    private static List<SubtitleItem> BuildPlainTextSrtItems(IReadOnlyList<SubtitleItem> subtitles)
    {
        var renderedSubtitles = new List<SubtitleItem>(subtitles.Count);

        foreach (var subtitle in subtitles)
        {
            var translatedText = subtitle.TranslatedLines.Count > 0
                ? string.Join("\\N", subtitle.TranslatedLines)
                : string.Join("\\N", subtitle.Lines);
            var plainTextLines = ConvertToPlainTextLines(translatedText);
            if (plainTextLines.Count == 0 ||
                plainTextLines.All(SubtitleFormatterService.IsAssDrawingCommand))
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

    private static List<string> ConvertToPlainTextLines(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return [];
        }

        var normalized = SubtitleFormatterService.NormalizeLineBreaks(translatedText)
            .Replace("\\n", "\\N", StringComparison.Ordinal);
        var segments = normalized.Split("\\N", StringSplitOptions.None);
        var lines = new List<string>();

        foreach (var segment in segments)
        {
            var plainText = SubtitleFormatterService.RemoveMarkup(segment);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                continue;
            }

            lines.AddRange(plainText.SplitIntoLines(42));
        }

        return lines;
    }

    private static bool TryResolveExistingSourceFile(TranslationRequest request, out string sourcePath)
    {
        sourcePath = string.Empty;
        if (string.IsNullOrWhiteSpace(request.SubtitleToTranslate) ||
            !File.Exists(request.SubtitleToTranslate))
        {
            return false;
        }

        if (!string.Equals(request.SourceSnapshotType, "external", StringComparison.OrdinalIgnoreCase))
        {
            sourcePath = request.SubtitleToTranslate;
            return true;
        }

        var info = new FileInfo(request.SubtitleToTranslate);
        if (request.SourceSnapshotFileSizeBytes.HasValue &&
            request.SourceSnapshotFileSizeBytes.Value != info.Length)
        {
            return false;
        }

        if (request.SourceSnapshotLastWriteUtc.HasValue)
        {
            var delta = (request.SourceSnapshotLastWriteUtc.Value - info.LastWriteTimeUtc).Duration();
            if (delta > TimeSpan.FromSeconds(2))
            {
                return false;
            }
        }

        sourcePath = request.SubtitleToTranslate;
        return true;
    }

    private bool TrySelectWritableOutputPath(
        TranslationRequest request,
        string sourcePath,
        string outputFormat,
        string subtitleTag,
        string subtitleTagShort,
        out string outputPath)
    {
        outputPath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        var normalizedOutputFormat = SubtitleOutputModeHelper.NormalizeFormat(outputFormat);
        foreach (var knownPath in GetKnownGeneratedPaths(request)
                     .Where(path => SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(path))
                         .Equals(normalizedOutputFormat, StringComparison.OrdinalIgnoreCase)))
        {
            if (IsSourcePath(knownPath, request.SubtitleToTranslate))
            {
                continue;
            }

            if (!File.Exists(knownPath) || IsLingarrManagedPath(knownPath, request, subtitleTag, subtitleTagShort))
            {
                outputPath = knownPath;
                return true;
            }
        }

        foreach (var candidate in _subtitleService.CreateFallbackPaths(
                     sourcePath,
                     request.TargetLanguage,
                     subtitleTag,
                     subtitleTagShort,
                     outputFormat))
        {
            if (!File.Exists(candidate) || IsLingarrManagedPath(candidate, request, subtitleTag, subtitleTagShort))
            {
                outputPath = candidate;
                return true;
            }
        }

        return false;
    }

    private static string? FindExistingOutputPath(
        TranslationRequest request,
        IReadOnlyCollection<Subtitles> matchingSubtitles,
        string format,
        string subtitleTag,
        string subtitleTagShort)
    {
        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(format);
        var knownPaths = GetKnownGeneratedPaths(request)
            .Where(path => SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(path))
                .Equals(normalizedFormat, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var knownPath = knownPaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(knownPath))
        {
            return knownPath;
        }

        if (knownPaths.Count > 0)
        {
            return null;
        }

        var targetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(request.TargetLanguage);
        return matchingSubtitles
            .Where(subtitle => SubtitleLanguageHelper.LanguageMatches(
                SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language),
                targetLanguage))
            .Where(subtitle => SubtitleOutputModeHelper.NormalizeFormat(
                    !string.IsNullOrWhiteSpace(subtitle.Format) ? subtitle.Format : Path.GetExtension(subtitle.Path))
                .Equals(normalizedFormat, StringComparison.OrdinalIgnoreCase))
            .Where(subtitle => IsLingarrManagedPath(subtitle.Path, request, subtitleTag, subtitleTagShort))
            .Select(subtitle => subtitle.Path)
            .FirstOrDefault(File.Exists);
    }

    private static bool IsLingarrManagedPath(
        string path,
        TranslationRequest request,
        string subtitleTag,
        string subtitleTagShort)
    {
        return GetKnownGeneratedPaths(request).Contains(path, StringComparer.OrdinalIgnoreCase) ||
               HasLingarrTag(path, subtitleTag, subtitleTagShort);
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

    private static bool SourceRequiresBothOutputs(TranslationRequest request)
    {
        return SubtitleOutputModeHelper.IsAssFormat(GetSourceFormat(request));
    }

    private static string GetSourceFormat(TranslationRequest request)
    {
        return SubtitleOutputModeHelper.NormalizeFormat(
            !string.IsNullOrWhiteSpace(request.SourceSubtitleFormat)
                ? request.SourceSubtitleFormat
                : Path.GetExtension(request.SubtitleToTranslate));
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

    private static void AddGeneratedOutput(
        TranslationRequest request,
        string outputPath,
        string outputFormat,
        bool preferAsPrimary)
    {
        var paths = GetKnownGeneratedPaths(request);
        if (!paths.Contains(outputPath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(outputPath);
        }

        var formats = SubtitleOutputModeHelper.DeserializeFormats(request.GeneratedOutputFormats)
            .ToList();
        if (formats.Count == 0 && !string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            formats.Add(SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(request.TranslatedSubtitle)));
        }

        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(outputFormat);
        if (!formats.Contains(normalizedFormat, StringComparer.OrdinalIgnoreCase))
        {
            formats.Add(normalizedFormat);
        }

        request.GeneratedSubtitlePaths = JsonSerializer.Serialize(paths);
        request.GeneratedOutputFormats = SubtitleOutputModeHelper.SerializeFormats(formats);
        if (preferAsPrimary)
        {
            request.TranslatedSubtitle = outputPath;
        }
    }

    private static void SetSequentialPositions(IReadOnlyList<SubtitleItem> subtitles)
    {
        for (var index = 0; index < subtitles.Count; index++)
        {
            subtitles[index].Position = index + 1;
        }
    }

    private static void MarkSkipped(SubtitleOutputBackfillResult result)
    {
        result.BackfillSkippedFiles++;
    }

    private static void MarkRequiresRetranslation(SubtitleOutputBackfillResult result)
    {
        result.BackfillSkippedFiles++;
        result.RequiresRetranslation = true;
    }

    private sealed record ResolvedBackfillSource(string Path, BackfillSourceKind SourceKind);

    private enum BackfillSourceKind
    {
        External,
        Embedded
    }
}
