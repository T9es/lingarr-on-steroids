using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;

namespace Lingarr.Server.Services;

public class SubtitleTranslationService
{
    private const int ProviderVisibleCharBudgetPerBatch = 20_000;
    private const int MaxBatchContextLines = 10;
    private const double ResidualEchoToleranceRatio = 0.02;
    private const int ResidualEchoToleranceCap = 25;
    private int _lastProgression = -1;
    private readonly ITranslationService _translationService;
    private readonly IProgressService? _progressService;
    private readonly IBatchFallbackService? _batchFallbackService;
    private readonly IDeferredRepairService? _deferredRepairService;
    private readonly ITranslationCheckpointService? _checkpointService;
    private readonly ILogger _logger;

    public SubtitleTranslationService(
        ITranslationService translationService,
        ILogger logger,
        IProgressService? progressService = null,
        IBatchFallbackService? batchFallbackService = null,
        IDeferredRepairService? deferredRepairService = null,
        ITranslationCheckpointService? checkpointService = null)
    {
        _translationService = translationService;
        _progressService = progressService;
        _batchFallbackService = batchFallbackService;
        _deferredRepairService = deferredRepairService;
        _checkpointService = checkpointService;
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
        var sourceFingerprint = GetCheckpointFingerprint(translationRequest);
        var checkpoint = _checkpointService == null
            ? null
            : await _checkpointService.LoadAsync(
                translationRequest.Id,
                sourceFingerprint,
                cancellationToken);
        var checkpointTranslations = checkpoint?.Translations ?? new Dictionary<int, string>();
        await ValidateAndCleanNonBatchCheckpointAsync(
            structureEntries,
            checkpoint,
            checkpointTranslations,
            translationRequest,
            cancellationToken);
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
            if (checkpointTranslations.TryGetValue(entry.Subtitle.Position, out var checkpointTranslation))
            {
                translated = checkpointTranslation;
            }
            else if (entry.IsTranslatable)
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
                translated = ValidateFreshProviderTranslation(
                    entry.Subtitle.Position,
                    entry.ProviderText,
                    translated,
                    translationRequest.SourceLanguage,
                    translationRequest.TargetLanguage);

                if (_checkpointService != null)
                {
                    await _checkpointService.SaveTranslationAsync(
                        translationRequest.Id,
                        sourceFingerprint,
                        entry.Subtitle.Position,
                        translated,
                        cancellationToken);
                }
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

            ApplyTranslationToNode(entry, translated);

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
            if (ex is ProviderPauseException)
            {
                throw;
            }

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
        var sourceFingerprint = GetCheckpointFingerprint(translationRequest);
        var checkpoint = _checkpointService == null
            ? null
            : await _checkpointService.LoadAsync(
                translationRequest.Id,
                sourceFingerprint,
                cancellationToken);
        var checkpointTranslations = checkpoint?.Translations ?? new Dictionary<int, string>();
        var providerTextByPosition = structureEntries.ToDictionary(entry => entry.Subtitle.Position, entry => entry.ProviderText);
        var analysisEntries = structureEntries
            .Select(entry => AssSubtitleSourceAnalyzer.CreateEntry(
                entry.Subtitle,
                entry.Structure,
                entry.ProviderText,
                entry.IsTranslatable,
                entry.RawSourceCharCount))
            .ToList();
        var analysis = AssSubtitleSourceAnalyzer.Analyze(analysisEntries);
        var globalDeduplication = ProviderTextDeduper.Deduplicate(
            structureEntries
                .Where(entry => entry.IsTranslatable)
                .Select(entry => new ProviderTextItem(entry.Subtitle.Position, entry.ProviderText))
                .ToList());
        var representativeProviderTranslations = new Dictionary<int, string>();
        await HydrateBatchCheckpointTranslationsAsync(
            globalDeduplication,
            checkpoint,
            checkpointTranslations,
            representativeProviderTranslations,
            translationRequest.SourceLanguage,
            translationRequest.TargetLanguage,
            fileIdentifier,
            cancellationToken);

        var representativeEntries = structureEntries
            .Where(entry =>
                entry.Kind == SubtitleTranslationNodeKind.Representative &&
                !representativeProviderTranslations.ContainsKey(entry.Subtitle.Position))
            .ToList();
        var representativeEntriesByPosition = representativeEntries.ToDictionary(
            entry => entry.Subtitle.Position,
            entry => entry);
        var batches = BuildBatches(representativeEntries, effectiveBatchSize, ProviderVisibleCharBudgetPerBatch);
        var translatableCueCount = structureEntries.Count(entry => entry.IsTranslatable);
        var skippedCueCount = structureEntries.Count - translatableCueCount;
        var rawSourceChars = structureEntries.Sum(entry => entry.RawSourceCharCount);
        var providerChars = structureEntries.Sum(entry => entry.Structure.ProviderVisibleCharCount);
        var representativeProviderChars = representativeEntries.Sum(entry => entry.Structure.ProviderVisibleCharCount);

        LogBatchPreparation(
            fileIdentifier,
            subtitles.Count,
            translatableCueCount,
            skippedCueCount,
            rawSourceChars,
            providerChars,
            representativeProviderChars,
            globalDeduplication,
            analysis,
            batches.Count);

        var processedRepresentatives = 0;
        var useDeferredRepair = batchRetryMode.Equals("deferred", StringComparison.OrdinalIgnoreCase) &&
                                _deferredRepairService != null;
        var useImmediateFallback = batchRetryMode.Equals("immediate", StringComparison.OrdinalIgnoreCase);
        var globalFailures = new List<RepairItem>();
        var deferredRepairEchoedPositions = new HashSet<int>();

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
                preContext = BuildBatchContext(
                    structureEntries,
                    batch.StartIndex,
                    ClampBatchContextCount(batchContextBefore),
                    true);
                postContext = BuildBatchContext(
                    structureEntries,
                    batch.EndIndex,
                    ClampBatchContextCount(batchContextAfter),
                    false);
            }

            var batchResult = await ProcessSubtitleBatchInternal(
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

            foreach (var translation in batchResult.ProviderTranslations)
            {
                representativeProviderTranslations[translation.Key] = translation.Value;
                if (_checkpointService != null)
                {
                    await _checkpointService.SaveTranslationAsync(
                        translationRequest.Id,
                        sourceFingerprint,
                        translation.Key,
                        translation.Value,
                        cancellationToken);
                }
            }

            if (useDeferredRepair && batchResult.Failures.Count > 0)
            {
                foreach (var failure in batchResult.Failures)
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

            processedRepresentatives += currentBatch.Count;
            var progressBase = representativeEntries.Count == 0
                ? 1.0
                : (double)processedRepresentatives / representativeEntries.Count;
            var progressPercent = useDeferredRepair
                ? progressBase * 0.95
                : progressBase;
            await EmitProgressDirect(translationRequest, progressPercent);
        }

        if (useDeferredRepair && globalFailures.Count > 0 && _deferredRepairService != null)
        {
            var repairDeduplication = ProviderTextDeduper.Deduplicate(
                globalFailures
                    .Select(failure =>
                    {
                        if (!representativeEntriesByPosition.TryGetValue(failure.Position, out var representativeEntry))
                        {
                            return null;
                        }

                        return new ProviderTextItem(failure.Position, representativeEntry.ProviderText);
                    })
                    .Where(item => item != null)
                    .Cast<ProviderTextItem>()
                    .ToList());
            var representativeFailures = globalFailures
                .Where(failure => repairDeduplication.IsRepresentative(failure.Position))
                .ToList();

            _logger.LogInformation(
                "[{FileId}] Deferred repair: {FailedCount} items collected from {BatchCount} batches, deduped to {RepresentativeCount} provider requests. Starting repair with context radius {Radius}.",
                fileIdentifier,
                globalFailures.Count,
                batches.Count,
                representativeFailures.Count,
                repairContextRadius);

            var repairBatch = _deferredRepairService.BuildContextualRepairBatch(
                representativeFailures,
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

            var repairSourceItems = representativeFailures
                .Where(failure => representativeEntriesByPosition.ContainsKey(failure.Position))
                .Select(failure => new BatchSubtitleItem
                {
                    Position = failure.Position,
                    Line = representativeEntriesByPosition[failure.Position].ProviderText
                })
                .ToList();
            var repairValidation = AnalyzeProviderTranslations(
                repairSourceItems,
                repairResults,
                translationRequest.SourceLanguage,
                translationRequest.TargetLanguage);
            var invalidRepairPositions = repairValidation.InvalidPositions;
            deferredRepairEchoedPositions.UnionWith(repairValidation.EchoedPositions);
            if (invalidRepairPositions.Count > 0)
            {
                _logger.LogWarning(
                    "[{FileId}] Deferred repair rejected {Count} source-echo or wrong-language result(s) at positions [{Positions}] before applying or checkpoint persistence.",
                    fileIdentifier,
                    invalidRepairPositions.Count,
                    string.Join(", ", invalidRepairPositions.OrderBy(position => position)));
            }

            foreach (var repairResult in repairResults)
            {
                if (invalidRepairPositions.Contains(repairResult.Key))
                {
                    continue;
                }

                representativeProviderTranslations[repairResult.Key] =
                    SubtitleTextStructure.NormalizeProviderTranslationText(repairResult.Value);
                if (_checkpointService != null)
                {
                    await _checkpointService.SaveTranslationAsync(
                        translationRequest.Id,
                        sourceFingerprint,
                        repairResult.Key,
                        repairResult.Value,
                        cancellationToken);
                }
            }

            _logger.LogInformation(
                "[{FileId}] Deferred repair completed: {RepairedCount} items repaired.",
                fileIdentifier,
                repairResults.Count);

            await EmitProgressDirect(translationRequest, 1.0);
        }

        var echoedRepresentativePositions = GetMostlyEchoedPositions(
            globalDeduplication.Representatives
                .Select(item => new BatchSubtitleItem
                {
                    Position = item.Position,
                    Line = item.ProviderText
                })
                .ToList(),
            representativeProviderTranslations,
            translationRequest.SourceLanguage,
            translationRequest.TargetLanguage,
            fileIdentifier,
            "final");
        echoedRepresentativePositions.UnionWith(deferredRepairEchoedPositions);
        foreach (var position in echoedRepresentativePositions)
        {
            representativeProviderTranslations.Remove(position);
        }
        var wrongLanguageRepresentativePositions = GetWrongTargetLanguagePositions(
            globalDeduplication.Representatives
                .Select(item => new BatchSubtitleItem
                {
                    Position = item.Position,
                    Line = item.ProviderText
                })
                .ToList(),
            representativeProviderTranslations,
            translationRequest.TargetLanguage,
            fileIdentifier,
            "final");
        foreach (var position in wrongLanguageRepresentativePositions)
        {
            representativeProviderTranslations.Remove(position);
        }

        ApplyRepresentativeTranslations(
            structureEntries,
            representativeProviderTranslations,
            globalDeduplication,
            fileIdentifier,
            "batch");

        ApplyMissingProviderFallbacks(
            structureEntries,
            representativeProviderTranslations,
            globalDeduplication,
            fileIdentifier);

        var unresolvedEntries = BuildUnresolvedEntries(
            structureEntries,
            representativeProviderTranslations,
            globalDeduplication);
        unresolvedEntries = ApplyResidualEchoTolerance(
            structureEntries,
            unresolvedEntries,
            globalDeduplication,
            echoedRepresentativePositions,
            translatableCueCount,
            fileIdentifier);
        if (unresolvedEntries.Count > 0)
        {
            ThrowMissingTranslationException(
                unresolvedEntries,
                item => echoedRepresentativePositions.Contains(
                    globalDeduplication.GetRepresentativePosition(item.Position)));
        }

        if (representativeEntries.Count == 0 || _lastProgression < 100)
        {
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
        var result = await ProcessSubtitleBatchInternal(
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
        return result.Failures;
    }

    private async Task<BatchProcessingResult> ProcessSubtitleBatchInternal(
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
            .Select((subtitle, index) =>
            {
                var structure = subtitleStructures != null && subtitleStructures.TryGetValue(subtitle.Position, out var preBuilt)
                    ? preBuilt
                    : SubtitleTextStructureFactory.Create(subtitle, stripSubtitleFormatting, preserveAssFormatting);
                var providerText = structure.ProviderVisibleText;
                var semanticClassification = SubtitleSemanticClassifier.Classify(
                    subtitle,
                    providerText,
                    subtitle.SsaDialogue?.Style);
                var isTranslatable = semanticClassification.ShouldRequestProvider;

                return new SubtitleTranslationNode(
                    index,
                    subtitle,
                    structure,
                    providerText,
                    isTranslatable,
                    0,
                    SubtitleTranslationNodeKind.Representative,
                    isTranslatable ? subtitle.Position : null,
                    semanticClassification.Kind,
                    semanticClassification.CanPreserveSourceWhenProviderMissing,
                    isTranslatable ? null : semanticClassification.Reason);
            })
            .ToList();

        var translatableItems = structureEntries
            .Where(entry => entry.IsTranslatable)
            .Select(entry => new ProviderTextItem(entry.Subtitle.Position, entry.ProviderText))
            .ToList();
        var deduplication = ProviderTextDeduper.Deduplicate(translatableItems);
        var batchItems = deduplication.Representatives
            .Select(item => new BatchSubtitleItem
            {
                Position = item.Position,
                Line = item.ProviderText
            })
            .ToList();

        if (batchItems.Count == 0)
        {
            foreach (var entry in structureEntries)
            {
                ApplyTranslationToNode(entry, entry.ProviderText);
            }

            return new BatchProcessingResult(new Dictionary<int, string>(), []);
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
                if (ex is ProviderPauseException)
                {
                    throw;
                }

                if (TranslationFailureClassifier.IsNonRepairableProviderConfigurationFailure(ex))
                {
                    var failureSummary = TranslationFailureClassifier.GetFailureSummary(ex);

                    _logger.LogError(
                        ex,
                        "[{FileId}] Batch {BatchNum} failed due to non-repairable provider configuration failure. Summary: {FailureSummary}",
                        fileIdentifier,
                        batchNumber,
                        failureSummary);

                    throw;
                }

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

                    return new BatchProcessingResult(new Dictionary<int, string>(), ExpandFailures(structureEntries, batchItems));
                }

                throw;
            }
        }

        var providerValidation = AnalyzeProviderTranslations(
            batchItems,
            batchResults,
            sourceLanguage,
            targetLanguage);
        var echoedPositions = GetMostlyEchoedPositions(
            batchItems,
            batchResults,
            sourceLanguage,
            targetLanguage,
            fileIdentifier,
            $"batch {batchNumber}/{totalBatches}");
        var wrongLanguagePositions = GetWrongTargetLanguagePositions(
            batchItems,
            batchResults,
            targetLanguage,
            fileIdentifier,
            $"batch {batchNumber}/{totalBatches}");
        var invalidProviderPositions = providerValidation.InvalidPositions.ToHashSet();
        invalidProviderPositions.UnionWith(echoedPositions);
        invalidProviderPositions.UnionWith(wrongLanguagePositions);
        if (providerValidation.MismatchedPositions.Count > 0 && wrongLanguagePositions.Count == 0)
        {
            _logger.LogWarning(
                "[{FileId}] Batch {BatchNum}/{TotalBatches}: rejected {Count} individual result(s) using the wrong target language at positions [{Positions}] before application or checkpoint persistence.",
                fileIdentifier,
                batchNumber,
                totalBatches,
                providerValidation.MismatchedPositions.Count,
                string.Join(", ", providerValidation.MismatchedPositions.OrderBy(position => position)));
        }
        var resolvedProviderTranslations = new Dictionary<int, string>();
        foreach (var entry in structureEntries.Where(entry => entry.IsTranslatable))
        {
            var representativePosition = deduplication.GetRepresentativePosition(entry.Subtitle.Position);
            if (invalidProviderPositions.Contains(representativePosition))
            {
                continue;
            }

            if (!batchResults.TryGetValue(representativePosition, out var translated) ||
                string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
            resolvedProviderTranslations[entry.Subtitle.Position] = translated;
            if (entry.Structure.VisibleLineCount > 1 && !entry.Structure.IsProviderTranslationCompatible(translated))
            {
                _logger.LogInformation(
                    "[{FileId}] Batch {BatchNum}/{TotalBatches}: translated subtitle {Position} was rewrapped by provider. Reflowing into {Expected} visible lines locally.",
                    fileIdentifier,
                    batchNumber,
                    totalBatches,
                    entry.Subtitle.Position,
                    entry.Structure.VisibleLineCount);
            }

            ApplyTranslationToNode(entry, translated);
        }

        var missingEntries = structureEntries
            .Where(entry =>
            {
                if (!entry.IsTranslatable)
                {
                    return false;
                }

                if (!resolvedProviderTranslations.TryGetValue(entry.Subtitle.Position, out var translated) ||
                    string.IsNullOrWhiteSpace(translated))
                {
                    if (!collectFailures && entry.CanPreserveSourceWhenProviderMissing)
                    {
                        ApplyTranslationToNode(entry, entry.ProviderText);
                        return false;
                    }

                    return true;
                }

                var translatedLinesMissing = entry.Subtitle.TranslatedLines == null ||
                                             entry.Subtitle.TranslatedLines.Count == 0 ||
                                             entry.Subtitle.TranslatedLines.All(string.IsNullOrWhiteSpace);
                if (!translatedLinesMissing)
                {
                    return false;
                }

                if (!collectFailures && entry.CanPreserveSourceWhenProviderMissing)
                {
                    ApplyTranslationToNode(entry, entry.ProviderText);
                    return false;
                }

                return true;
            })
            .Select(entry => new BatchSubtitleItem
            {
                Position = entry.Subtitle.Position,
                Line = entry.ProviderText
            })
            .ToList();

        if (missingEntries.Count == 0)
        {
            return new BatchProcessingResult(resolvedProviderTranslations, []);
        }

        if (collectFailures)
        {
            _logger.LogWarning(
                "[{FileId}] Batch {BatchNum}/{TotalBatches}: {Count} item(s) failed, collecting for deferred repair",
                fileIdentifier,
                batchNumber,
                totalBatches,
                missingEntries.Count);
            return new BatchProcessingResult(resolvedProviderTranslations, missingEntries);
        }

        ThrowMissingTranslationException(
            missingEntries,
            item => echoedPositions.Contains(deduplication.GetRepresentativePosition(item.Position)));
        return new BatchProcessingResult(resolvedProviderTranslations, []);
    }

    private async Task HydrateBatchCheckpointTranslationsAsync(
        ProviderTextDeduplicationResult deduplication,
        TranslationCheckpoint? checkpoint,
        Dictionary<int, string> checkpointTranslations,
        Dictionary<int, string> representativeProviderTranslations,
        string? sourceLanguage,
        string? targetLanguage,
        string fileIdentifier,
        CancellationToken cancellationToken)
    {
        var checkpointChanged = false;
        var invalidCheckpointRepresentatives = new HashSet<int>();
        var removedCheckpointPositions = new HashSet<int>();

        foreach (var representative in deduplication.Representatives)
        {
            var representativePosition = representative.Position;
            var sourceItem = new BatchSubtitleItem
            {
                Position = representativePosition,
                Line = representative.ProviderText
            };
            var candidates = new List<(int Position, string Translation, bool IsValid)>();
            foreach (var memberPosition in deduplication
                         .GetMemberPositions(representativePosition)
                         .OrderBy(position => position))
            {
                if (!checkpointTranslations.TryGetValue(memberPosition, out var checkpointTranslation))
                {
                    continue;
                }

                var normalizedTranslation = SubtitleTextStructure.NormalizeProviderTranslationText(checkpointTranslation);
                var candidateValidation = AnalyzeProviderTranslations(
                    [sourceItem],
                    new Dictionary<int, string>
                    {
                        [representativePosition] = normalizedTranslation
                    },
                    sourceLanguage,
                    targetLanguage);
                candidates.Add((
                    memberPosition,
                    normalizedTranslation,
                    candidateValidation.InvalidPositions.Count == 0));
            }

            var chosenCandidate = candidates
                .Where(candidate => candidate.IsValid)
                .OrderBy(candidate => candidate.Position == representativePosition ? 0 : 1)
                .ThenBy(candidate => candidate.Position)
                .FirstOrDefault();
            if (!chosenCandidate.IsValid)
            {
                if (candidates.Count > 0)
                {
                    invalidCheckpointRepresentatives.Add(representativePosition);
                }

                foreach (var memberPosition in deduplication.GetMemberPositions(representativePosition))
                {
                    if (checkpointTranslations.Remove(memberPosition))
                    {
                        checkpointChanged = true;
                        removedCheckpointPositions.Add(memberPosition);
                    }
                }

                continue;
            }

            representativeProviderTranslations[representativePosition] = chosenCandidate.Translation;
            if (!checkpointTranslations.TryGetValue(representativePosition, out var canonicalTranslation) ||
                !string.Equals(canonicalTranslation, chosenCandidate.Translation, StringComparison.Ordinal))
            {
                checkpointTranslations[representativePosition] = chosenCandidate.Translation;
                checkpointChanged = true;
            }

            foreach (var memberPosition in deduplication
                         .GetMemberPositions(representativePosition)
                         .Where(position => position != representativePosition))
            {
                if (checkpointTranslations.Remove(memberPosition))
                {
                    checkpointChanged = true;
                    removedCheckpointPositions.Add(memberPosition);
                }
            }
        }

        if (!checkpointChanged || checkpoint == null || _checkpointService == null)
        {
            return;
        }

        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _checkpointService.SaveCheckpointAsync(checkpoint, cancellationToken);
        _logger.LogWarning(
            "[{FileId}] Canonicalized {RepresentativeCount} checkpoint representative(s), removed {CheckpointCount} stale or invalid checkpoint key(s), and retained {InvalidRepresentativeCount} representative(s) for fresh translation at positions [{Positions}].",
            fileIdentifier,
            deduplication.Representatives.Count,
            removedCheckpointPositions.Count,
            invalidCheckpointRepresentatives.Count,
            string.Join(", ", invalidCheckpointRepresentatives.OrderBy(position => position)));
    }

    private async Task ValidateAndCleanNonBatchCheckpointAsync(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        TranslationCheckpoint? checkpoint,
        Dictionary<int, string> checkpointTranslations,
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        if (checkpoint == null || _checkpointService == null || checkpointTranslations.Count == 0)
        {
            return;
        }

        var cachedSourceItems = structureEntries
            .Where(entry => entry.IsTranslatable && checkpointTranslations.ContainsKey(entry.Subtitle.Position))
            .GroupBy(entry => entry.Subtitle.Position)
            .Select(group => group.First())
            .Select(entry => new BatchSubtitleItem
            {
                Position = entry.Subtitle.Position,
                Line = entry.ProviderText
            })
            .ToList();
        var cachedTranslations = cachedSourceItems.ToDictionary(
            item => item.Position,
            item => checkpointTranslations[item.Position]);
        var invalidPositions = AnalyzeProviderTranslations(
            cachedSourceItems,
            cachedTranslations,
            translationRequest.SourceLanguage,
            translationRequest.TargetLanguage).InvalidPositions;
        if (invalidPositions.Count == 0)
        {
            return;
        }

        foreach (var position in invalidPositions)
        {
            checkpointTranslations.Remove(position);
        }

        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _checkpointService.SaveCheckpointAsync(checkpoint, cancellationToken);
        _logger.LogWarning(
            "Removed {Count} invalid checkpoint translation(s) at positions [{Positions}] for request {RequestId} before single-line translation.",
            invalidPositions.Count,
            string.Join(", ", invalidPositions.OrderBy(position => position)),
            translationRequest.Id);
    }

    private static ProviderTranslationValidationResult AnalyzeProviderTranslations(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translatedByPosition,
        string? sourceLanguage,
        string? targetLanguage)
    {
        var validation = ProviderTranslationValidation.Analyze(
            sourceItems,
            translatedByPosition,
            sourceLanguage,
            targetLanguage);
        var invalidPositions = validation.InvalidPositions.ToHashSet();
        foreach (var sourceItem in sourceItems)
        {
            if (!translatedByPosition.TryGetValue(sourceItem.Position, out var translated) ||
                string.IsNullOrWhiteSpace(translated))
            {
                invalidPositions.Add(sourceItem.Position);
            }
        }

        return new ProviderTranslationValidationResult(
            invalidPositions,
            validation.EchoedPositions,
            validation.MismatchedPositions);
    }

    private static string ValidateFreshProviderTranslation(
        int position,
        string sourceText,
        string translatedText,
        string? sourceLanguage,
        string? targetLanguage)
    {
        var normalizedTranslation = SubtitleTextStructure.NormalizeProviderTranslationText(translatedText);
        var sourceItem = new BatchSubtitleItem
        {
            Position = position,
            Line = sourceText
        };
        var validation = AnalyzeProviderTranslations(
            [sourceItem],
            new Dictionary<int, string>
            {
                [position] = normalizedTranslation
            },
            sourceLanguage,
            targetLanguage);
        if (!validation.InvalidPositions.Contains(position))
        {
            return normalizedTranslation;
        }

        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(normalizedTranslation))
        {
            reasons.Add("the provider returned an empty translation");
        }

        if (validation.EchoedPositions.Contains(position))
        {
            reasons.Add("the provider echoed the source text");
        }

        if (validation.MismatchedPositions.Contains(position))
        {
            reasons.Add("the provider returned the wrong target language");
        }

        throw new TranslationException(
            $"Provider returned an invalid translation for subtitle position {position}: {string.Join(" and ", reasons)}. " +
            "The result was not saved or applied.");
    }

    private HashSet<int> GetMostlyEchoedPositions(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translations,
        string sourceLanguage,
        string targetLanguage,
        string fileIdentifier,
        string phase)
    {
        var analysis = TranslationEchoGuard.AnalyzeBatch(
            sourceItems,
            translations,
            sourceLanguage,
            targetLanguage);
        if (analysis.EchoedCount == 0)
        {
            return [];
        }

        if (analysis.IsMostlyEchoed)
        {
            _logger.LogWarning(
                "[{FileId}] {Phase}: provider output appears to echo source text. Unchanged comparable cues: {Echoed}/{Comparable} ({Ratio:P0}); strongest unchanged cluster: {ClusterEchoed}/{ClusterComparable} ({ClusterRatio:P0}). Treating affected items as missing translations.",
                fileIdentifier,
                phase,
                analysis.EchoedCount,
                analysis.ComparableCount,
                analysis.EchoRatio,
                analysis.ClusterEchoedCount,
                analysis.ClusterComparableCount,
                analysis.ClusterEchoRatio);
        }
        else
        {
            _logger.LogWarning(
                "[{FileId}] {Phase}: provider echoed {Echoed} substantial dialogue cue(s) at positions [{Positions}]. Treating affected items as missing translations.",
                fileIdentifier,
                phase,
                analysis.EchoedCount,
                string.Join(", ", analysis.EchoedPositions.Take(10)));
        }

        return analysis.EchoedPositions.ToHashSet();
    }

    private HashSet<int> GetWrongTargetLanguagePositions(
        IReadOnlyList<BatchSubtitleItem> sourceItems,
        IReadOnlyDictionary<int, string> translations,
        string targetLanguage,
        string fileIdentifier,
        string phase)
    {
        var analysis = TranslationLanguageGuard.AnalyzeBatch(
            sourceItems,
            translations,
            targetLanguage);
        if (!analysis.IsMostlyMismatched)
        {
            return [];
        }

        _logger.LogWarning(
            "[{FileId}] {Phase}: provider output appears to use the wrong target language. Expected {Expected}, observed {Observed}. Mismatched comparable cues: {Mismatched}/{Comparable} ({Ratio:P0}); strongest mismatched cluster: {ClusterMismatched}/{ClusterComparable} ({ClusterRatio:P0}). Treating affected items as missing translations.",
            fileIdentifier,
            phase,
            analysis.ExpectedDescription,
            analysis.ObservedDescription,
            analysis.MismatchedCount,
            analysis.ComparableCount,
            analysis.MismatchRatio,
            analysis.ClusterMismatchedCount,
            analysis.ClusterComparableCount,
            analysis.ClusterMismatchRatio);

        return analysis.MismatchedPositions.ToHashSet();
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

    private static void ThrowMissingTranslationException(
        List<BatchSubtitleItem> missingItems,
        Func<BatchSubtitleItem, bool>? isAutoApprovalEligible = null)
    {
        var missingCues = missingItems
            .Select(item => new MissingTranslationCue(
                item.Position,
                item.Line,
                isAutoApprovalEligible?.Invoke(item) ?? false))
            .ToList();
        throw new MissingTranslationException(missingCues);
    }

    private static List<string> BuildBatchContext(
        IReadOnlyList<SubtitleTranslationNode> entries,
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

    private static int ClampBatchContextCount(int count)
    {
        return Math.Clamp(count, 0, MaxBatchContextLines);
    }

    private static List<string> BuildContext(
        IReadOnlyList<SubtitleTranslationNode> subtitles,
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

    private List<SubtitleTranslationNode> BuildStructureEntries(
        List<SubtitleItem> subtitles,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        return SubtitleTranslationNodePlanner.Plan(
                subtitles,
                stripSubtitleFormatting,
                preserveAssFormatting)
            .Nodes
            .ToList();
    }

    private void LogBatchPreparation(
        string fileIdentifier,
        int sourceCount,
        int translatableCueCount,
        int skippedCueCount,
        int rawSourceChars,
        int providerChars,
        int representativeProviderChars,
        ProviderTextDeduplicationResult deduplication,
        AssSubtitleSourceAnalysis analysis,
        int batchCount)
    {
        _logger.LogInformation(
            "[{FileId}] Batch translation prep: source subtitles={SourceCount}, translatable cues={TranslatableCount}, skipped cues={SkippedCount}, raw source chars={RawChars}, provider chars={ProviderChars}, provider request chars={ProviderRequestChars}, unique provider texts={UniqueProviderTexts}, duplicate translatable cues={DuplicateTranslatableCount}, drawing cues={DrawingCount}, avg provider chars/cue={AverageCharsPerCue:F2}, final batch count={BatchCount}",
            fileIdentifier,
            sourceCount,
            translatableCueCount,
            skippedCueCount,
            rawSourceChars,
            providerChars,
            representativeProviderChars,
            deduplication.RepresentativeCount,
            deduplication.DuplicatePositionCount,
            analysis.DrawingEvents,
            analysis.AverageProviderCharsPerTranslatableCue,
            batchCount);

        if (analysis.IsPathological)
        {
            _logger.LogWarning(
                "[{FileId}] ASS source looks pathological: highDrawingDensity={HighDrawingDensity}, highDuplicateDensity={HighDuplicateDensity}, fragmentedText={FragmentedText}, dominantStyle={DominantStyle} ({DominantStyleCount})",
                fileIdentifier,
                analysis.HasHighDrawingDensity,
                analysis.HasHighDuplicateDensity,
                analysis.HasFragmentedText,
                analysis.DominantStyleName ?? "<none>",
                analysis.DominantStyleCount);
        }
    }

    private void ApplyRepresentativeTranslations(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        IReadOnlyDictionary<int, string> representativeProviderTranslations,
        ProviderTextDeduplicationResult deduplication,
        string fileIdentifier,
        string phase)
    {
        foreach (var entry in structureEntries)
        {
            if (!entry.IsTranslatable)
            {
                ApplyTranslationToNode(entry, entry.ProviderText);
                continue;
            }

            var representativePosition = deduplication.GetRepresentativePosition(entry.Subtitle.Position);
            if (!representativeProviderTranslations.TryGetValue(representativePosition, out var translated) ||
                string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
            if (entry.Structure.VisibleLineCount > 1 && !entry.Structure.IsProviderTranslationCompatible(translated))
            {
                _logger.LogInformation(
                    "[{FileId}] {Phase}: translated subtitle {Position} was rewrapped by provider. Reflowing into {Expected} visible lines locally.",
                    fileIdentifier,
                    phase,
                    entry.Subtitle.Position,
                    entry.Structure.VisibleLineCount);
            }

            ApplyTranslationToNode(entry, translated);
        }
    }

    private static void ApplyTranslationToNode(SubtitleTranslationNode entry, string translated)
    {
        if (!entry.IsTranslatable)
        {
            entry.Subtitle.TranslatedLines = entry.Structure.SourceLines.ToList();
            return;
        }

        translated = SubtitleTextStructure.NormalizeProviderTranslationText(translated);
        var translatedLines = entry.Structure.ApplyProviderTranslation(translated);
        if (TranslatedLinesAreEmpty(translatedLines))
        {
            translatedLines = entry.Structure.ApplyProviderTranslationAsSingleVisibleText(translated);
        }

        entry.Subtitle.TranslatedLines = translatedLines;
    }

    private static bool TranslatedLinesAreEmpty(List<string>? translatedLines)
    {
        return translatedLines == null ||
               translatedLines.Count == 0 ||
               translatedLines.All(string.IsNullOrWhiteSpace);
    }

    private static List<BatchSubtitleItem> BuildUnresolvedEntries(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        IReadOnlyDictionary<int, string> representativeProviderTranslations,
        ProviderTextDeduplicationResult deduplication)
    {
        return structureEntries
            .Where(entry =>
            {
                if (!entry.IsTranslatable)
                {
                    return false;
                }

                if (entry.CanPreserveSourceWhenProviderMissing)
                {
                    return false;
                }

                var representativePosition = deduplication.GetRepresentativePosition(entry.Subtitle.Position);
                if (!representativeProviderTranslations.TryGetValue(representativePosition, out var translated) ||
                    string.IsNullOrWhiteSpace(translated))
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
    }

    private void ApplyMissingProviderFallbacks(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        IReadOnlyDictionary<int, string> representativeProviderTranslations,
        ProviderTextDeduplicationResult deduplication,
        string fileIdentifier)
    {
        var fallbackCount = 0;
        foreach (var entry in structureEntries.Where(entry =>
                     entry.IsTranslatable &&
                     entry.CanPreserveSourceWhenProviderMissing))
        {
            var representativePosition = deduplication.GetRepresentativePosition(entry.Subtitle.Position);
            if (representativeProviderTranslations.TryGetValue(representativePosition, out var translated) &&
                !string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            ApplyTranslationToNode(entry, entry.ProviderText);
            fallbackCount++;
        }

        if (fallbackCount > 0)
        {
            _logger.LogWarning(
                "[{FileId}] Preserved {Count} provider-omitted non-dialogue subtitle node(s) locally instead of dropping cues.",
                fileIdentifier,
                fallbackCount);
        }
    }

    private List<BatchSubtitleItem> ApplyResidualEchoTolerance(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        List<BatchSubtitleItem> unresolvedEntries,
        ProviderTextDeduplicationResult deduplication,
        IReadOnlySet<int> echoedRepresentativePositions,
        int totalTranslatableCueCount,
        string fileIdentifier)
    {
        if (unresolvedEntries.Count == 0 || echoedRepresentativePositions.Count == 0)
        {
            return unresolvedEntries;
        }

        var allUnresolvedWereFinalEchoes = unresolvedEntries.All(item =>
            echoedRepresentativePositions.Contains(deduplication.GetRepresentativePosition(item.Position)));
        if (!allUnresolvedWereFinalEchoes)
        {
            return unresolvedEntries;
        }

        var toleranceLimit = CalculateResidualEchoTolerance(totalTranslatableCueCount);
        if (unresolvedEntries.Count > toleranceLimit)
        {
            return unresolvedEntries;
        }

        var entriesByPosition = structureEntries.ToDictionary(entry => entry.Subtitle.Position);
        foreach (var item in unresolvedEntries)
        {
            if (entriesByPosition.TryGetValue(item.Position, out var entry))
            {
                ApplyTranslationToNode(entry, entry.ProviderText);
            }
        }

        _logger.LogWarning(
            "[{FileId}] Preserved {Count} residual unchanged subtitle cue(s) within the {Ratio:P0} final echo tolerance (limit {Limit}/{Total}).",
            fileIdentifier,
            unresolvedEntries.Count,
            ResidualEchoToleranceRatio,
            toleranceLimit,
            totalTranslatableCueCount);

        return [];
    }

    private static int CalculateResidualEchoTolerance(int totalTranslatableCueCount)
    {
        if (totalTranslatableCueCount <= 0)
        {
            return 0;
        }

        var ratioLimit = (int)Math.Ceiling(totalTranslatableCueCount * ResidualEchoToleranceRatio);
        return Math.Min(ResidualEchoToleranceCap, Math.Max(1, ratioLimit));
    }

    private static List<BatchSubtitleItem> ExpandFailures(
        IReadOnlyList<SubtitleTranslationNode> structureEntries,
        IReadOnlyList<BatchSubtitleItem> representativeFailures)
    {
        var entriesByPosition = structureEntries.ToDictionary(entry => entry.Subtitle.Position);
        return representativeFailures
            .SelectMany(failure =>
            {
                if (!entriesByPosition.TryGetValue(failure.Position, out var entry))
                {
                    return [failure];
                }

                return structureEntries
                    .Where(candidate =>
                        candidate.IsTranslatable &&
                        string.Equals(candidate.ProviderText, entry.ProviderText, StringComparison.Ordinal))
                    .Select(candidate => new BatchSubtitleItem
                    {
                        Position = candidate.Subtitle.Position,
                        Line = candidate.ProviderText
                    });
            })
            .DistinctBy(item => item.Position)
            .OrderBy(item => item.Position)
            .ToList();
    }

    private static List<SubtitleBatchSlice> BuildBatches(
        IReadOnlyList<SubtitleTranslationNode> entries,
        int maxCueCount,
        int maxProviderChars)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var batches = new List<SubtitleBatchSlice>();
        var currentEntries = new List<SubtitleTranslationNode>();
        var currentProviderChars = 0;
        var currentTranslatableCount = 0;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var entryProviderChars = entry.IsTranslatable ? entry.Structure.ProviderVisibleCharCount : 0;
            var exceedsCueLimit = entry.IsTranslatable &&
                                  currentTranslatableCount > 0 &&
                                  currentTranslatableCount + 1 > maxCueCount;
            var exceedsCharLimit = currentProviderChars > 0 &&
                                   currentProviderChars + entryProviderChars > maxProviderChars;

            if (currentEntries.Count > 0 && (exceedsCueLimit || exceedsCharLimit))
            {
                batches.Add(
                    new SubtitleBatchSlice(
                        currentEntries[0].GlobalIndex,
                        currentEntries[^1].GlobalIndex,
                        currentEntries,
                        currentProviderChars));
                currentEntries = [];
                currentProviderChars = 0;
                currentTranslatableCount = 0;
            }

            currentEntries.Add(entry);
            currentProviderChars += entryProviderChars;
            if (entry.IsTranslatable)
            {
                currentTranslatableCount++;
            }
        }

        if (currentEntries.Count > 0)
        {
            batches.Add(
                new SubtitleBatchSlice(
                    currentEntries[0].GlobalIndex,
                    currentEntries[^1].GlobalIndex,
                    currentEntries,
                    currentProviderChars));
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

    private static string GetCheckpointFingerprint(TranslationRequest translationRequest)
    {
        if (!string.IsNullOrWhiteSpace(translationRequest.SourceSnapshotFingerprint))
        {
            return translationRequest.SourceSnapshotFingerprint;
        }

        return string.Join(
            "|",
            translationRequest.SubtitleToTranslate ?? string.Empty,
            translationRequest.SourceLanguage,
            translationRequest.TargetLanguage,
            translationRequest.SourceSubtitleFormat ?? string.Empty);
    }

    private sealed record BatchProcessingResult(
        Dictionary<int, string> ProviderTranslations,
        List<BatchSubtitleItem> Failures);

    private sealed record SubtitleBatchSlice(
        int StartIndex,
        int EndIndex,
        List<SubtitleTranslationNode> Entries,
        int ProviderCharCount);
}
