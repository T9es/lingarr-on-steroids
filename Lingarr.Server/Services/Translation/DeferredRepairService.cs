using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Implements deferred repair of failed batch translations.
/// Collects failed items and retries them together at the end with surrounding context.
/// </summary>
public class DeferredRepairService : IDeferredRepairService
{
    private readonly ILogger<DeferredRepairService> _logger;

    public DeferredRepairService(ILogger<DeferredRepairService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ContextualRepairBatch BuildContextualRepairBatch(
        List<RepairItem> failedItems,
        List<SubtitleItem> allSubtitles,
        int contextRadius,
        IReadOnlyDictionary<int, string> providerVisibleTextByPosition)
    {
        if (failedItems.Count == 0)
        {
            return new ContextualRepairBatch();
        }

        // Build a lookup for all subtitles by position
        var subtitlesByPosition = allSubtitles.ToDictionary(s => s.Position);
        var maxPosition = allSubtitles.Max(s => s.Position);
        var minPosition = allSubtitles.Min(s => s.Position);
        
        // Get failed positions sorted
        var failedPositions = failedItems.Select(f => f.Position).OrderBy(p => p).ToList();
        var failedSet = new HashSet<int>(failedPositions);
        
        // Build merged context ranges
        var ranges = BuildMergedRanges(failedPositions, contextRadius, minPosition, maxPosition);
        
        _logger.LogDebug(
            "Building repair batch: {FailedCount} failed items, context radius {Radius}, merged into {RangeCount} range(s)",
            failedItems.Count, contextRadius, ranges.Count);

        // Build the batch items from ranges
        var batchItems = new List<BatchSubtitleItem>();
        var includedPositions = new HashSet<int>();
        
        foreach (var range in ranges)
        {
            for (int pos = range.Start; pos <= range.End; pos++)
            {
                if (includedPositions.Contains(pos))
                {
                    continue; // Already included from a previous range
                }
                
                if (!subtitlesByPosition.TryGetValue(pos, out var subtitle))
                {
                    continue; // Position doesn't exist (sparse positions)
                }

                var line = providerVisibleTextByPosition.TryGetValue(pos, out var providerVisibleText)
                    ? providerVisibleText
                    : FallbackToVisibleText(subtitle);
                
                batchItems.Add(new BatchSubtitleItem
                {
                    Position = pos,
                    Line = line
                });
                
                includedPositions.Add(pos);
            }
        }
        
        // Sort by position to maintain order
        batchItems = batchItems.OrderBy(b => b.Position).ToList();
        
        _logger.LogInformation(
            "Repair batch built: {TotalItems} items ({FailedCount} failed + {ContextCount} context)",
            batchItems.Count, failedSet.Count, batchItems.Count - failedSet.Count);

        return new ContextualRepairBatch
        {
            Items = batchItems,
            FailedPositions = failedSet,
            Ranges = ranges
        };
    }

    private static string FallbackToVisibleText(SubtitleItem subtitle)
    {
        if (subtitle.PlaintextLines.Count > 0)
        {
            return string.Join('\n', subtitle.PlaintextLines);
        }

        if (subtitle.Lines.Count > 0)
        {
            var cleaned = subtitle.Lines.Select(SubtitleFormatterService.RemoveMarkup).ToList();
            return string.Join('\n', cleaned);
        }

        return string.Empty;
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, string>> ExecuteRepairAsync(
        ContextualRepairBatch repairBatch,
        IBatchTranslationService batchService,
        IBatchFallbackService fallbackService,
        string sourceLanguage,
        string targetLanguage,
        int batchSize,
        int maxRetries,
        string fileIdentifier,
        CancellationToken cancellationToken)
    {
        if (repairBatch.Items.Count == 0 || repairBatch.FailedPositions.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        maxRetries = Math.Max(1, maxRetries);
        batchSize = batchSize <= 0 ? 50 : batchSize;
        
        var results = new Dictionary<int, string>();
        
        _logger.LogInformation(
            "[{FileId}] Starting deferred repair: {FailedCount} failed items with {ContextCount} context items. Using batch size {BatchSize}.",
            fileIdentifier, repairBatch.FailedPositions.Count, 
            repairBatch.Items.Count - repairBatch.FailedPositions.Count, batchSize);

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                _logger.LogDebug(
                    "[{FileId}] Repair attempt {Attempt}/{MaxAttempts}",
                    fileIdentifier, attempt, maxRetries + 1);
                
                // Identify which failed items are still missing
                var stillMissingPositions = repairBatch.FailedPositions
                    .Where(p => !results.ContainsKey(p) || string.IsNullOrWhiteSpace(results[p]))
                    .ToHashSet();

                if (stillMissingPositions.Count == 0) break;

                var repairRequests = BuildRepairRequests(repairBatch, stillMissingPositions);
                var chunks = SplitIntoChunks(repairRequests, batchSize);
                
                _logger.LogInformation(
                    "[{FileId}] Repair attempt {Attempt}: Processing {RequestCount} failed representatives in {ChunkCount} scheduling chunk(s) of max size {BatchSize}",
                    fileIdentifier, attempt, repairRequests.Count, chunks.Count, batchSize);

                var requestNumber = 0;
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    foreach (var repairRequest in chunk)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        requestNumber++;

                        var chunkResults = await fallbackService.TranslateWithFallbackAsync(
                            [repairRequest],
                            batchService,
                            sourceLanguage,
                            targetLanguage,
                            3,
                            fileIdentifier,
                            requestNumber,
                            repairRequests.Count,
                            cancellationToken);

                        if (repairBatch.FailedPositions.Contains(repairRequest.Position) &&
                            chunkResults.TryGetValue(repairRequest.Position, out var translated) &&
                            !string.IsNullOrWhiteSpace(translated))
                        {
                            results[repairRequest.Position] = translated;
                        }
                    }
                }
                
                // Check if all failed items were translated
                var finalMissing = repairBatch.FailedPositions
                    .Where(p => !results.ContainsKey(p) || string.IsNullOrWhiteSpace(results[p]))
                    .ToList();
                
                if (finalMissing.Count == 0)
                {
                    _logger.LogInformation(
                        "[{FileId}] Deferred repair returned candidates for all {Count} failed items on attempt {Attempt}",
                        fileIdentifier, repairBatch.FailedPositions.Count, attempt);
                    return results;
                }
                
                if (attempt <= maxRetries)
                {
                    _logger.LogWarning(
                        "[{FileId}] Repair attempt {Attempt} incomplete: {MissingCount} items still missing. Retrying...",
                        fileIdentifier, attempt, finalMissing.Count);
                }
                else
                {
                    _logger.LogError(
                        "[{FileId}] Deferred repair exhausted after {Attempts} attempts. {MissingCount} items failed permanently.",
                        fileIdentifier, attempt, finalMissing.Count);
                    
                    throw CreateMissingTranslationException(
                        repairBatch,
                        results,
                        $"Deferred repair failed after {attempt} attempts. " +
                        $"{finalMissing.Count} items could not be translated.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TranslationException ex) when (attempt > maxRetries)
            {
                if (ex is MissingTranslationException ||
                    ex is ProviderPauseException ||
                    TranslationFailureClassifier.IsProviderUnavailable(ex))
                {
                    throw;
                }

                throw CreateMissingTranslationException(
                    repairBatch,
                    results,
                    $"Deferred repair failed after {attempt} attempts.",
                    ex);
            }
            catch (Exception ex)
            {
                if (ex is ProviderPauseException)
                {
                    throw;
                }
                if (TranslationFailureClassifier.IsNonRepairableProviderConfigurationFailure(ex))
                {
                    _logger.LogError(
                        ex,
                        "[{FileId}] Deferred repair hit a non-repairable provider configuration failure on attempt {Attempt}. Failing fast.",
                        fileIdentifier,
                        attempt);
                    throw CreateMissingTranslationException(
                        repairBatch,
                        results,
                        $"Deferred repair failed after {attempt} attempts because the translation provider configuration is invalid.",
                        ex);
                }

                if (attempt <= maxRetries)
                {
                    _logger.LogWarning(ex,
                        "[{FileId}] Error during repair attempt {Attempt}. Retrying...",
                        fileIdentifier, attempt);
                }
                else
                {
                    _logger.LogError(ex, "[{FileId}] Permanent error during repair attempt {Attempt}", fileIdentifier, attempt);
                    if (TranslationFailureClassifier.IsProviderUnavailable(ex))
                    {
                        throw;
                    }

                    throw CreateMissingTranslationException(
                        repairBatch,
                        results,
                        $"Deferred repair failed after {attempt} attempts.",
                        ex);
                }
            }
        }
        
        return results;
    }

    private static MissingTranslationException CreateMissingTranslationException(
        ContextualRepairBatch repairBatch,
        IReadOnlyDictionary<int, string> results,
        string message,
        Exception? innerException = null)
    {
        var missingCues = repairBatch.FailedPositions
            .Where(position =>
                !results.TryGetValue(position, out var translation) ||
                string.IsNullOrWhiteSpace(translation))
            .OrderBy(position => position)
            .Select(position => new MissingTranslationCue(
                position,
                repairBatch.Items.FirstOrDefault(item => item.Position == position)?.Line ?? string.Empty,
                AutoApprovalEligible: false))
            .ToList();

        if (missingCues.Count == 0)
        {
            missingCues = repairBatch.FailedPositions
                .OrderBy(position => position)
                .Select(position => new MissingTranslationCue(
                    position,
                    repairBatch.Items.FirstOrDefault(item => item.Position == position)?.Line ?? string.Empty,
                    AutoApprovalEligible: false))
                .ToList();
        }

        return new MissingTranslationException(
            missingCues,
            innerException ?? new TranslationException(message));
    }

    private static List<BatchSubtitleItem> BuildRepairRequests(
        ContextualRepairBatch repairBatch,
        IReadOnlySet<int> failedPositions)
    {
        var itemsByPosition = repairBatch.Items.ToDictionary(item => item.Position);
        var requests = new List<BatchSubtitleItem>(failedPositions.Count);

        foreach (var position in failedPositions.OrderBy(position => position))
        {
            if (!itemsByPosition.TryGetValue(position, out var failedItem))
            {
                continue;
            }

            var range = repairBatch.Ranges.FirstOrDefault(candidate =>
                position >= candidate.Start && position <= candidate.End);
            var rangeStart = range?.Start ?? repairBatch.Items.Min(item => item.Position);
            var rangeEnd = range?.End ?? repairBatch.Items.Max(item => item.Position);

            var preContext = repairBatch.Items
                .Where(item => item.Position >= rangeStart && item.Position < position)
                .OrderBy(item => item.Position)
                .Select(item => item.Line)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            var postContext = repairBatch.Items
                .Where(item => item.Position > position && item.Position <= rangeEnd)
                .OrderBy(item => item.Position)
                .Select(item => item.Line)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            requests.Add(new ContextualBatchSubtitleItem(
                failedItem,
                preContext.Count == 0 ? null : preContext,
                postContext.Count == 0 ? null : postContext));
        }

        return requests;
    }

    private static List<List<BatchSubtitleItem>> SplitIntoChunks(List<BatchSubtitleItem> items, int chunkSize)
    {
        var chunks = new List<List<BatchSubtitleItem>>();
        for (int i = 0; i < items.Count; i += chunkSize)
        {
            chunks.Add(items.Skip(i).Take(chunkSize).ToList());
        }
        return chunks;
    }

    /// <summary>
    /// Builds merged context ranges for the given failed positions.
    /// Adjacent failures share context to avoid duplication.
    /// </summary>
    private static List<ContextRange> BuildMergedRanges(
        List<int> failedPositions, 
        int contextRadius, 
        int minPosition, 
        int maxPosition)
    {
        var ranges = new List<ContextRange>();
        
        foreach (var position in failedPositions)
        {
            var rangeStart = Math.Max(minPosition, position - contextRadius);
            var rangeEnd = Math.Min(maxPosition, position + contextRadius);
            
            // Check if this range overlaps or is adjacent to the last range
            if (ranges.Count > 0 && ranges[^1].End >= rangeStart - 1)
            {
                // Merge with the last range
                ranges[^1] = new ContextRange(ranges[^1].Start, Math.Max(ranges[^1].End, rangeEnd));
            }
            else
            {
                // Add a new range
                ranges.Add(new ContextRange(rangeStart, rangeEnd));
            }
        }
        
        return ranges;
    }
}
