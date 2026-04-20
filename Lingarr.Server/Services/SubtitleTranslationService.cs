using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Services;

public class SubtitleTranslationService
{
    private const int ProviderVisibleCharBudgetPerBatch = 20_000;
    private int _lastProgression = -1;
    private readonly ITranslationService _translationService;
    private readonly IProgressService? _progressService;
    private readonly IBatchFallbackService? _batchFallbackService;
    private readonly IDeferredRepairService? _deferredRepairService;
    private readonly ILogger _logger;

    public SubtitleTranslationService(
        ITranslationService translationService,
        ILogger logger,
        IProgressService? progressService = null,
        IBatchFallbackService? batchFallbackService = null,
        IDeferredRepairService? deferredRepairService = null)
    {
        _translationService = translationService;
        _progressService = progressService;
        _batchFallbackService = batchFallbackService;
        _deferredRepairService = deferredRepairService;
        _logger = logger;
    }

    public async Task<List<SubtitleItem>> TranslateSubtitles(
        List<SubtitleItem> subtitles,
        TranslationRequest translationRequest,
        bool stripSubtitleFormatting,
        int contextBefore,
        int contextAfter,
        bool preserveAssFormatting,
        CancellationToken cancellationToken)
    {
        if (_progressService == null)
        {
            throw new TranslationException("Subtitle translator could not be initialized, progress service is null.");
        }

        var structureEntries = BuildStructureEntries(subtitles, stripSubtitleFormatting, preserveAssFormatting);
        var iteration = 0;
        var totalSubtitles = subtitles.Count;

        for (var index = 0; index < structureEntries.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _lastProgression = -1;
                break;
            }

            var entry = structureEntries[index];
            var contextLinesBefore = BuildContext(structureEntries, index, contextBefore, true);
            var contextLinesAfter = BuildContext(structureEntries, index, contextAfter, false);

            var translated = entry.ProviderText;
            if (entry.IsTranslatable)
            {
                translated = await TranslateSubtitleLine(
                    new TranslateAbleSubtitleLine
                    {
                        SubtitleLine = entry.ProviderText,
                        SourceLanguage = translationRequest.SourceLanguage,
                        TargetLanguage = translationRequest.TargetLanguage,
                        ContextLinesBefore = contextLinesBefore.Count > 0 ? contextLinesBefore : null,
                        ContextLinesAfter = contextLinesAfter.Count > 0 ? contextLinesAfter : null
                    },
                    cancellationToken);
            }

            translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
            if (entry.Structure.VisibleLineCount > 1 &&
                !entry.Structure.IsProviderTranslationCompatible(translated))
            {
                _logger.LogWarning(
                    "Single-line translation output line mismatch at position {Position}. Expected {Expected} visible lines.",
                    entry.Subtitle.Position,
                    entry.Structure.VisibleLineCount);
            }

            entry.Subtitle.TranslatedLines = entry.Structure.ApplyProviderTranslation(translated);

            iteration++;
            await EmitProgress(translationRequest, iteration, totalSubtitles);
        }

        _lastProgression = -1;
        return subtitles;
    }

    public async Task<string> TranslateSubtitleLine(
        TranslateAbleSubtitleLine translateAbleSubtitle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _translationService.TranslateAsync(
                translateAbleSubtitle.SubtitleLine,
                translateAbleSubtitle.SourceLanguage,
                translateAbleSubtitle.TargetLanguage,
                translateAbleSubtitle.ContextLinesBefore,
                translateAbleSubtitle.ContextLinesAfter,
                cancellationToken);
        }
        catch (TranslationException ex)
        {
            _logger.LogError(
                ex,
                "Translation failed for subtitle line: {SubtitleLine} from {SourceLang} to {TargetLang}",
                translateAbleSubtitle.SubtitleLine,
                translateAbleSubtitle.SourceLanguage,
                translateAbleSubtitle.TargetLanguage);
            throw new TranslationException("Translation failed for subtitle line", ex);
        }
    }

    public async Task<List<SubtitleItem>> TranslateSubtitlesBatch(
        List<SubtitleItem> subtitles,
        TranslationRequest translationRequest,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting = false,
        int batchSize = 0,
        string batchRetryMode = "deferred",
        int maxSplitAttempts = 3,
        int repairContextRadius = 10,
        int repairMaxRetries = 1,
        bool batchContextEnabled = false,
        int batchContextBefore = 3,
        int batchContextAfter = 3,
        string fileIdentifier = "",
        CancellationToken cancellationToken = default)
    {
        if (_progressService == null)
        {
            throw new TranslationException("Subtitle translator could not be initialized, progress service is null.");
        }

        if (_translationService is not IBatchTranslationService batchTranslationService)
        {
            throw new TranslationException("The configured translation service does not support batch translation.");
        }

        var effectiveBatchSize = batchSize <= 0 ? subtitles.Count : batchSize;
        var structureEntries = BuildStructureEntries(subtitles, stripSubtitleFormatting, preserveAssFormatting);
        var structuresByPosition = structureEntries.ToDictionary(entry => entry.Subtitle.Position, entry => entry.Structure);
        var providerTextByPosition = structureEntries.ToDictionary(entry => entry.Subtitle.Position, entry => entry.ProviderText);
        var batches = BuildBatches(structureEntries, effectiveBatchSize, ProviderVisibleCharBudgetPerBatch);
        var translatableCueCount = structureEntries.Count(entry => entry.IsTranslatable);
        var skippedCueCount = structureEntries.Count - translatableCueCount;
        var rawSourceChars = structureEntries.Sum(entry => entry.RawSourceCharCount);
        var providerChars = structureEntries.Sum(entry => entry.Structure.ProviderVisibleCharCount);

        _logger.LogInformation(
            "[{FileId}] Batch translation prep: source subtitles={SourceCount}, translatable cues={TranslatableCount}, skipped cues={SkippedCount}, raw source chars={RawChars}, provider chars={ProviderChars}, final batch count={BatchCount}",
            fileIdentifier,
            subtitles.Count,
            translatableCueCount,
            skippedCueCount,
            rawSourceChars,
            providerChars,
            batches.Count);

        var processedSubtitles = 0;
        var useDeferredRepair = batchRetryMode.Equals("deferred", StringComparison.OrdinalIgnoreCase) &&
                                _deferredRepairService != null;
        var useImmediateFallback = batchRetryMode.Equals("immediate", StringComparison.OrdinalIgnoreCase);
        var globalFailures = new List<RepairItem>();

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _lastProgression = -1;
                break;
            }

            var batch = batches[batchIndex];
            var currentBatch = batch.Entries.Select(entry => entry.Subtitle).ToList();
            var structureLookup = batch.Entries.ToDictionary(entry => entry.Subtitle.Position, entry => entry.Structure);

            List<string>? preContext = null;
            List<string>? postContext = null;
            if (batchContextEnabled)
            {
                preContext = BuildBatchContext(structureEntries, batch.StartIndex, batchContextBefore, true);
                postContext = BuildBatchContext(structureEntries, batch.EndIndex, batchContextAfter, false);
            }

            var batchFailures = await ProcessSubtitleBatchInternal(
                currentBatch,
                batchTranslationService,
                translationRequest.SourceLanguage,
                translationRequest.TargetLanguage,
                stripSubtitleFormatting,
                useImmediateFallback,
                maxSplitAttempts,
                useDeferredRepair,
                preContext,
                postContext,
                fileIdentifier,
                batchIndex + 1,
                batches.Count,
                preserveAssFormatting,
                cancellationToken,
                structureLookup);

            if (useDeferredRepair && batchFailures.Count > 0)
            {
                foreach (var failure in batchFailures)
                {
                    globalFailures.Add(
                        new RepairItem
                        {
                            Position = failure.Position,
                            OriginalLine = failure.Line,
                            OriginalBatchIndex = batchIndex + 1
                        });
                }
            }

            processedSubtitles += currentBatch.Count;
            var progressPercent = useDeferredRepair
                ? (double)processedSubtitles / subtitles.Count * 0.95
                : (double)processedSubtitles / subtitles.Count;
            await EmitProgressDirect(translationRequest, progressPercent);
        }

        if (useDeferredRepair && globalFailures.Count > 0 && _deferredRepairService != null)
        {
            _logger.LogInformation(
                "[{FileId}] Deferred repair: {FailedCount} items collected from {BatchCount} batches. Starting repair with context radius {Radius}.",
                fileIdentifier,
                globalFailures.Count,
                batches.Count,
                repairContextRadius);

            var repairBatch = _deferredRepairService.BuildContextualRepairBatch(
                globalFailures,
                subtitles,
                repairContextRadius,
                providerTextByPosition);

            var repairResults = await _deferredRepairService.ExecuteRepairAsync(
                repairBatch,
                batchTranslationService,
                _batchFallbackService ?? throw new TranslationException("Batch fallback service is required for repair."),
                translationRequest.SourceLanguage,
                translationRequest.TargetLanguage,
                effectiveBatchSize,
                repairMaxRetries,
                fileIdentifier,
                cancellationToken);

            var unresolvedFailures = new List<BatchSubtitleItem>();
            foreach (var failedItem in globalFailures)
            {
                if (!structuresByPosition.TryGetValue(failedItem.Position, out var structure))
                {
                    unresolvedFailures.Add(new BatchSubtitleItem
                    {
                        Position = failedItem.Position,
                        Line = failedItem.OriginalLine
                    });
                    continue;
                }

                if (!repairResults.TryGetValue(failedItem.Position, out var translated))
                {
                    unresolvedFailures.Add(new BatchSubtitleItem
                    {
                        Position = failedItem.Position,
                        Line = structure.ProviderVisibleText
                    });
                    continue;
                }

                translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
                if (structure.VisibleLineCount > 1 && !structure.IsProviderTranslationCompatible(translated))
                {
                    unresolvedFailures.Add(new BatchSubtitleItem
                    {
                        Position = failedItem.Position,
                        Line = structure.ProviderVisibleText
                    });
                    continue;
                }

                var subtitle = subtitles.FirstOrDefault(item => item.Position == failedItem.Position);
                if (subtitle == null)
                {
                    unresolvedFailures.Add(new BatchSubtitleItem
                    {
                        Position = failedItem.Position,
                        Line = structure.ProviderVisibleText
                    });
                    continue;
                }

                subtitle.TranslatedLines = structure.ApplyProviderTranslation(translated);
            }

            if (unresolvedFailures.Count > 0)
            {
                ThrowMissingTranslationException(unresolvedFailures);
            }

            _logger.LogInformation(
                "[{FileId}] Deferred repair completed: {RepairedCount} items repaired.",
                fileIdentifier,
                repairResults.Count);

            await EmitProgressDirect(translationRequest, 1.0);
        }

        _lastProgression = -1;
        return subtitles;
    }

    public async Task<List<BatchSubtitleItem>> ProcessSubtitleBatch(
        List<SubtitleItem> currentBatch,
        IBatchTranslationService batchTranslationService,
        string sourceLanguage,
        string targetLanguage,
        bool stripSubtitleFormatting,
        bool enableFallback = false,
        int maxSplitAttempts = 3,
        bool collectFailures = false,
        List<string>? preContext = null,
        List<string>? postContext = null,
        string fileIdentifier = "",
        int batchNumber = 1,
        int totalBatches = 1,
        bool preserveAssFormatting = false,
        CancellationToken cancellationToken = default)
    {
        return await ProcessSubtitleBatchInternal(
            currentBatch,
            batchTranslationService,
            sourceLanguage,
            targetLanguage,
            stripSubtitleFormatting,
            enableFallback,
            maxSplitAttempts,
            collectFailures,
            preContext,
            postContext,
            fileIdentifier,
            batchNumber,
            totalBatches,
            preserveAssFormatting,
            cancellationToken,
            null);
    }

    private async Task<List<BatchSubtitleItem>> ProcessSubtitleBatchInternal(
        List<SubtitleItem> currentBatch,
        IBatchTranslationService batchTranslationService,
        string sourceLanguage,
        string targetLanguage,
        bool stripSubtitleFormatting,
        bool enableFallback,
        int maxSplitAttempts,
        bool collectFailures,
        List<string>? preContext,
        List<string>? postContext,
        string fileIdentifier,
        int batchNumber,
        int totalBatches,
        bool preserveAssFormatting,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, SubtitleTextStructure>? subtitleStructures)
    {
        var structureEntries = currentBatch
            .Select(subtitle =>
            {
                var structure = subtitleStructures != null && subtitleStructures.TryGetValue(subtitle.Position, out var preBuilt)
                    ? preBuilt
                    : BuildSubtitleTextStructure(subtitle, stripSubtitleFormatting, preserveAssFormatting);
                var providerText = structure.ProviderVisibleText;
                return new SubtitleStructureEntry(
                    subtitle,
                    structure,
                    providerText,
                    IsMeaningfullyTranslatable(providerText),
                    0);
            })
            .ToList();

        var batchItems = structureEntries
            .Where(entry => entry.IsTranslatable)
            .Select(entry => new BatchSubtitleItem
            {
                Position = entry.Subtitle.Position,
                Line = entry.ProviderText
            })
            .ToList();

        if (batchItems.Count == 0)
        {
            return [];
        }

        Dictionary<int, string> batchResults;
        if (enableFallback && _batchFallbackService != null)
        {
            _logger.LogDebug(
                "[{FileId}] Using batch fallback service with max {MaxSplitAttempts} split attempts",
                fileIdentifier,
                maxSplitAttempts);
            batchResults = await _batchFallbackService.TranslateWithFallbackAsync(
                batchItems,
                batchTranslationService,
                sourceLanguage,
                targetLanguage,
                maxSplitAttempts,
                fileIdentifier,
                batchNumber,
                totalBatches,
                cancellationToken);
        }
        else
        {
            try
            {
                batchResults = await batchTranslationService.TranslateBatchAsync(
                    batchItems,
                    sourceLanguage,
                    targetLanguage,
                    preContext,
                    postContext,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                if (collectFailures)
                {
                    var failureType = TranslationFailureClassifier.IsProviderUnavailable(ex)
                        ? "translation provider unavailability"
                        : "batch translation/parsing failure";
                    var failureSummary = TranslationFailureClassifier.GetFailureSummary(ex);

                    _logger.LogError(
                        ex,
                        "[{FileId}] Batch {BatchNum} failed completely due to {FailureType}. Marking all {Count} items for deferred repair. Summary: {FailureSummary}",
                        fileIdentifier,
                        batchNumber,
                        failureType,
                        batchItems.Count,
                        failureSummary);

                    return batchItems;
                }

                throw;
            }
        }

        var failedByCompatibility = new HashSet<int>();
        foreach (var entry in structureEntries.Where(entry => entry.IsTranslatable))
        {
            if (!batchResults.TryGetValue(entry.Subtitle.Position, out var translated) ||
                string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
            if (entry.Structure.VisibleLineCount > 1 && !entry.Structure.IsProviderTranslationCompatible(translated))
            {
                failedByCompatibility.Add(entry.Subtitle.Position);
                _logger.LogWarning(
                    "[{FileId}] Batch {BatchNum}/{TotalBatches}: translated line mismatch for subtitle {Position}. Expected {Expected} visible lines.",
                    fileIdentifier,
                    batchNumber,
                    totalBatches,
                    entry.Subtitle.Position,
                    entry.Structure.VisibleLineCount);
                continue;
            }

            entry.Subtitle.TranslatedLines = entry.Structure.ApplyProviderTranslation(translated);
        }

        var missingEntries = structureEntries
            .Where(entry =>
            {
                if (!entry.IsTranslatable)
                {
                    return false;
                }

                if (!batchResults.TryGetValue(entry.Subtitle.Position, out var translated) ||
                    string.IsNullOrWhiteSpace(translated))
                {
                    return true;
                }

                if (failedByCompatibility.Contains(entry.Subtitle.Position))
                {
                    return true;
                }

                return entry.Subtitle.TranslatedLines == null ||
                       entry.Subtitle.TranslatedLines.Count == 0 ||
                       entry.Subtitle.TranslatedLines.All(string.IsNullOrWhiteSpace);
            })
            .Select(entry => new BatchSubtitleItem
            {
                Position = entry.Subtitle.Position,
                Line = entry.ProviderText
            })
            .ToList();

        if (missingEntries.Count == 0)
        {
            return [];
        }

        if (collectFailures)
        {
            _logger.LogWarning(
                "[{FileId}] Batch {BatchNum}/{TotalBatches}: {Count} item(s) failed, collecting for deferred repair",
                fileIdentifier,
                batchNumber,
                totalBatches,
                missingEntries.Count);
            return missingEntries;
        }

        ThrowMissingTranslationException(missingEntries);
        return [];
    }

    private async Task EmitProgressDirect(TranslationRequest translationRequest, double progressPercent)
    {
        if (_progressService == null)
        {
            return;
        }

        var percentage = (int)(progressPercent * 100);
        if (percentage != _lastProgression)
        {
            _lastProgression = percentage;
            await _progressService.Emit(translationRequest, percentage);
        }
    }

    private static void ThrowMissingTranslationException(List<BatchSubtitleItem> missingItems)
    {
        var positionRange = missingItems.Count <= 5
            ? string.Join(", ", missingItems.Select(item => item.Position))
            : $"{string.Join(", ", missingItems.Take(5).Select(item => item.Position))}... (+{missingItems.Count - 5} more)";

        var example = missingItems[0];
        var exampleText = example.Line.Length > 80 ? example.Line[..77] + "..." : example.Line;
        var message =
            $"Translation failed: {missingItems.Count} subtitle(s) missing at positions: {positionRange}. Example original text at position {example.Position}: \"{exampleText}\"";
        throw new TranslationException(message);
    }

    private static List<string> BuildBatchContext(
        IReadOnlyList<SubtitleStructureEntry> entries,
        int edgeIndex,
        int count,
        bool before)
    {
        if (count <= 0)
        {
            return [];
        }

        var context = new List<string>(count);
        if (before)
        {
            var start = Math.Max(0, edgeIndex - count);
            for (var index = start; index < edgeIndex; index++)
            {
                var providerText = entries[index].ProviderText;
                if (!string.IsNullOrWhiteSpace(providerText))
                {
                    context.Add(providerText);
                }
            }
        }
        else
        {
            var end = Math.Min(entries.Count - 1, edgeIndex + count);
            for (var index = edgeIndex + 1; index <= end; index++)
            {
                var providerText = entries[index].ProviderText;
                if (!string.IsNullOrWhiteSpace(providerText))
                {
                    context.Add(providerText);
                }
            }
        }

        return context;
    }

    private static List<string> BuildContext(
        IReadOnlyList<SubtitleStructureEntry> subtitles,
        int startIndex,
        int count,
        bool isBeforeContext)
    {
        if (count <= 0)
        {
            return [];
        }

        var context = new List<string>();
        var start = isBeforeContext
            ? Math.Max(0, startIndex - count)
            : startIndex + 1;
        var end = isBeforeContext
            ? startIndex
            : Math.Min(subtitles.Count, startIndex + 1 + count);

        for (var index = start; index < end; index++)
        {
            var providerText = subtitles[index].ProviderText;
            if (!string.IsNullOrWhiteSpace(providerText))
            {
                context.Add(providerText);
            }
        }

        return context;
    }

    private static bool IsMeaningfullyTranslatable(string providerText)
    {
        return !string.IsNullOrWhiteSpace(providerText) &&
               !SubtitleFormatterService.IsMeaningless(providerText.Trim());
    }

    private List<SubtitleStructureEntry> BuildStructureEntries(
        List<SubtitleItem> subtitles,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var entries = new List<SubtitleStructureEntry>(subtitles.Count);
        foreach (var subtitle in subtitles)
        {
            var structure = BuildSubtitleTextStructure(subtitle, stripSubtitleFormatting, preserveAssFormatting);
            var providerText = structure.ProviderVisibleText;
            var rawSourceLines = GetSourceLines(subtitle, stripSubtitleFormatting, preserveAssFormatting);
            var rawSourceChars = string.Join(" ", rawSourceLines).Length;

            entries.Add(
                new SubtitleStructureEntry(
                    subtitle,
                    structure,
                    providerText,
                    IsMeaningfullyTranslatable(providerText),
                    rawSourceChars));
        }

        return entries;
    }

    private static SubtitleTextStructure BuildSubtitleTextStructure(
        SubtitleItem subtitle,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var sourceLines = GetSourceLines(subtitle, stripSubtitleFormatting, preserveAssFormatting);
        if (sourceLines.Count == 0)
        {
            return new SubtitleTextStructure(SubtitleStructureMode.PlainText, [string.Empty], [
                new SubtitleTextLine(
                    0,
                    0,
                    [new SubtitleTextPart(SubtitleTextPartKind.Text, string.Empty, true, string.Empty)],
                    string.Empty)
            ]);
        }

        if (stripSubtitleFormatting && !preserveAssFormatting)
        {
            var plainLines = sourceLines
                .Select((line, index) => new SubtitleTextLine(
                    index,
                    0,
                    [new SubtitleTextPart(SubtitleTextPartKind.Text, line, true, line)],
                    string.Empty))
                .ToList();
            return new SubtitleTextStructure(SubtitleStructureMode.PlainText, sourceLines, plainLines);
        }

        if (IsAssSubtitle(subtitle))
        {
            var assLines = new AssTextStructureParser().Parse(sourceLines);
            return new SubtitleTextStructure(SubtitleStructureMode.Ass, sourceLines, assLines);
        }

        var inlineLines = new InlineMarkupStructureParser().Parse(sourceLines);
        return new SubtitleTextStructure(SubtitleStructureMode.InlineMarkup, sourceLines, inlineLines);
    }

    private static bool IsAssSubtitle(SubtitleItem subtitle)
    {
        return subtitle.SsaDialogue != null || subtitle.SsaFormat != null;
    }

    private static List<string> GetSourceLines(
        SubtitleItem subtitle,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var usePlaintextInput = stripSubtitleFormatting && !preserveAssFormatting;
        return usePlaintextInput
            ? subtitle.PlaintextLines
            : subtitle.Lines;
    }

    private static List<SubtitleBatchSlice> BuildBatches(
        IReadOnlyList<SubtitleStructureEntry> entries,
        int maxCueCount,
        int maxProviderChars)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var batches = new List<SubtitleBatchSlice>();
        var currentEntries = new List<SubtitleStructureEntry>();
        var currentProviderChars = 0;
        var batchStart = 0;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var entryProviderChars = entry.IsTranslatable ? entry.Structure.ProviderVisibleCharCount : 0;
            var exceedsCueLimit = currentEntries.Count >= maxCueCount;
            var exceedsCharLimit = currentProviderChars > 0 &&
                                   currentProviderChars + entryProviderChars > maxProviderChars;

            if (currentEntries.Count > 0 && (exceedsCueLimit || exceedsCharLimit))
            {
                batches.Add(new SubtitleBatchSlice(batchStart, index - 1, currentEntries, currentProviderChars));
                currentEntries = [];
                currentProviderChars = 0;
                batchStart = index;
            }

            currentEntries.Add(entry);
            currentProviderChars += entryProviderChars;
        }

        if (currentEntries.Count > 0)
        {
            batches.Add(new SubtitleBatchSlice(batchStart, entries.Count - 1, currentEntries, currentProviderChars));
        }

        return batches;
    }

    private async Task EmitProgress(TranslationRequest request, int iteration, int total)
    {
        var progress = (int)Math.Round((double)iteration * 100 / total);
        if (progress == _lastProgression)
        {
            return;
        }

        var progressBar = BuildProgressBar(progress);
        _logger.LogInformation(
            "{ProgressBar} {Progress}% ({Current}/{Total})",
            progressBar,
            progress,
            iteration,
            total);

        await _progressService!.Emit(request, progress);
        _lastProgression = progress;
    }

    private static string BuildProgressBar(int percentage, int width = 30)
    {
        var filled = (int)Math.Round((double)percentage * width / 100);
        var empty = width - filled;
        return $"[|Green|{new string('█', filled)}|/Green||Orange|{new string('░', empty)}|/Orange|]";
    }

    private sealed record SubtitleStructureEntry(
        SubtitleItem Subtitle,
        SubtitleTextStructure Structure,
        string ProviderText,
        bool IsTranslatable,
        int RawSourceCharCount);

    private sealed record SubtitleBatchSlice(
        int StartIndex,
        int EndIndex,
        List<SubtitleStructureEntry> Entries,
        int ProviderCharCount);
}
