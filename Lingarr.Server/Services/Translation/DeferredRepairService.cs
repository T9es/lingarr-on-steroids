using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;

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
        bool stripSubtitleFormatting)
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
                
                var line = string.Join(" ", stripSubtitleFormatting 
                    ? subtitle.PlaintextLines 
                    : subtitle.Lines);
                
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

    /// <inheritdoc />
    public async Task<Dictionary<int, string>> ExecuteRepairAsync(
        ContextualRepairBatch repairBatch,
        IBatchTranslationService batchService,
        string sourceLanguage,
        string targetLanguage,
        int maxRetries,
        string fileIdentifier,
        CancellationToken cancellationToken)
    {
        if (repairBatch.Items.Count == 0 || repairBatch.FailedPositions.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        maxRetries = Math.Max(1, maxRetries);
        var results = new Dictionary<int, string>();
        
        _logger.LogInformation(
            "[{FileId}] Starting deferred repair: {FailedCount} failed items with {ContextCount} context items",
            fileIdentifier, repairBatch.FailedPositions.Count, 
            repairBatch.Items.Count - repairBatch.FailedPositions.Count);

        for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                _logger.LogDebug(
                    "[{FileId}] Repair attempt {Attempt}/{MaxAttempts}",
                    fileIdentifier, attempt, maxRetries + 1);
                
                // Note: Deferred repair includes context in the batch items themselves,
                // so we don't use wrapper context here
                var batchResults = await batchService.TranslateBatchAsync(
                    repairBatch.Items,
                    sourceLanguage,
                    targetLanguage,
                    null,  // preContext - context is already in batch items
                    null,  // postContext - context is already in batch items
                    cancellationToken);
                
                // Extract only the translations for failed positions
                foreach (var position in repairBatch.FailedPositions)
                {
                    if (batchResults.TryGetValue(position, out var translated) && 
                        !string.IsNullOrWhiteSpace(translated))
                    {
                        results[position] = translated;
                    }
                }
                
                // Check if all failed items were translated
                var stillMissing = repairBatch.FailedPositions
                    .Where(p => !results.ContainsKey(p) || string.IsNullOrWhiteSpace(results[p]))
                    .ToList();
                
                if (stillMissing.Count == 0)
                {
                    _logger.LogInformation(
                        "[{FileId}] Deferred repair succeeded: all {Count} items translated on attempt {Attempt}",
                        fileIdentifier, repairBatch.FailedPositions.Count, attempt);
                    return results;
                }
                
                if (attempt <= maxRetries)
                {
                    _logger.LogWarning(
                        "[{FileId}] Repair attempt {Attempt} incomplete: {MissingCount} items still missing. Retrying...",
                        fileIdentifier, attempt, stillMissing.Count);
                }
                else
                {
                    _logger.LogError(
                        "[{FileId}] Deferred repair exhausted after {Attempts} attempts. {MissingCount} items failed permanently.",
                        fileIdentifier, attempt, stillMissing.Count);
                    
                    // Log sample of failed items
                    var sampleFailed = string.Join("; ", stillMissing.Take(5)
                        .Select(p => $"[pos {p}]"));
                    _logger.LogError(
                        "[{FileId}] Sample of failed positions: {Positions}",
                        fileIdentifier, sampleFailed);
                    
                    throw new TranslationException(
                        $"Deferred repair failed after {attempt} attempts. " +
                        $"{stillMissing.Count} items could not be translated.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TranslationException) when (attempt > maxRetries)
            {
                throw;
            }
            catch (TranslationException ex) when (attempt <= maxRetries)
            {
                _logger.LogWarning(ex,
                    "[{FileId}] Repair attempt {Attempt} failed with error. Retrying...",
                    fileIdentifier, attempt);
            }
            catch (Exception ex) when (attempt <= maxRetries)
            {
                _logger.LogWarning(ex,
                    "[{FileId}] Unexpected error during repair attempt {Attempt}. Retrying...",
                    fileIdentifier, attempt);
            }
        }
        
        throw new TranslationException("Deferred repair failed after maximum retry attempts.");
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
