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
        batchSize = batchSize <= 0 ? 50 : batchSize; // Default to 50 if zero or negative
        
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

                // Filter repairBatch.Items to only include current ranges that contain missing failed positions
                // However, the repairBatch already contains merged ranges. 
                // To keep it simple and respect the user's batching request:
                // We will split the ENTIRE repairBatch.Items (failed + context) into smaller chunks
                // and process each chunk using the fallback service.

                var chunks = SplitIntoChunks(repairBatch.Items, batchSize);
                
                _logger.LogInformation(
                    "[{FileId}] Repair attempt {Attempt}: Processing {ChunkCount} chunks of max size {BatchSize}",
                    fileIdentifier, attempt, chunks.Count, batchSize);

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    
                    // Note: Instead of direct TranslateBatchAsync, we use TranslateWithFallbackAsync
                    // to gracefully handle any individual chunk failure (like JSON truncation)
                    var chunkResults = await fallbackService.TranslateWithFallbackAsync(
                        chunk,
                        batchService,
                        sourceLanguage,
                        targetLanguage,
                        3, // maxSplitAttempts for repairs
                        fileIdentifier,
                        i + 1,
                        chunks.Count,
                        cancellationToken);

                    // Extract translations for failed positions that were in this chunk
                    foreach (var item in chunk)
                    {
                        if (repairBatch.FailedPositions.Contains(item.Position) && 
                            chunkResults.TryGetValue(item.Position, out var translated) && 
                            !string.IsNullOrWhiteSpace(translated))
                        {
                            results[item.Position] = translated;
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
                        "[{FileId}] Deferred repair succeeded: all {Count} items translated on attempt {Attempt}",
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
                    
                    throw new TranslationException(
                        $"Deferred repair failed after {attempt} attempts. " +
                        $"{finalMissing.Count} items could not be translated.");
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
            catch (Exception ex)
            {
                if (attempt <= maxRetries)
                {
                    _logger.LogWarning(ex,
                        "[{FileId}] Error during repair attempt {Attempt}. Retrying...",
                        fileIdentifier, attempt);
                }
                else
                {
                    _logger.LogError(ex, "[{FileId}] Permanent error during repair attempt {Attempt}", fileIdentifier, attempt);
                    throw;
                }
            }
        }
        
        return results;
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
