using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Provides fallback translation with graduated chunk splitting when batch translations fail.
/// </summary>
public class BatchFallbackService : IBatchFallbackService
{
    private readonly ILogger<BatchFallbackService> _logger;

    public BatchFallbackService(ILogger<BatchFallbackService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, string>> TranslateWithFallbackAsync(
        List<BatchSubtitleItem> batch,
        IBatchTranslationService batchService,
        string sourceLanguage,
        string targetLanguage,
        int maxSplitAttempts,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        // Ensure at least 1 attempt (full batch retry)
        maxSplitAttempts = Math.Max(1, maxSplitAttempts);
        
        var results = new Dictionary<int, string>();
        var failedItems = new List<BatchSubtitleItem>(batch);

        for (int splitLevel = 1; splitLevel <= maxSplitAttempts; splitLevel++)
        {
            if (failedItems.Count == 0)
            {
                break;
            }

            var chunks = SplitIntoChunks(failedItems, splitLevel);
            var stillFailed = new List<BatchSubtitleItem>();

            _logger.LogInformation(
                "Batch fallback: Attempting split level {Level}/{Max}, processing {ChunkCount} chunk(s) with {ItemCount} total items",
                splitLevel, maxSplitAttempts, chunks.Count, failedItems.Count);

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var chunkResults = await batchService.TranslateBatchAsync(
                        chunk, sourceLanguage, targetLanguage, cancellationToken);

                    foreach (var kvp in chunkResults)
                    {
                        results[kvp.Key] = kvp.Value;
                    }

                    _logger.LogDebug(
                        "Chunk succeeded at split level {Level}: {Count} items translated",
                        splitLevel, chunkResults.Count);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TranslationException ex)
                {
                    _logger.LogWarning(ex,
                        "Chunk failed at split level {Level}: {Count} items. Will retry with smaller chunks if available.",
                        splitLevel, chunk.Count);
                    stillFailed.AddRange(chunk);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Unexpected error in chunk at split level {Level}: {Count} items",
                        splitLevel, chunk.Count);
                    stillFailed.AddRange(chunk);
                }
            }

            failedItems = stillFailed;

            if (failedItems.Count > 0 && splitLevel < maxSplitAttempts)
            {
                _logger.LogInformation(
                    "Batch fallback: {Count} items still failed, will retry with split level {NextLevel}",
                    failedItems.Count, splitLevel + 1);
            }
        }

        // Fail the task if items still remain after all split attempts
        if (failedItems.Count > 0)
        {
            _logger.LogError(
                "Batch fallback exhausted after {Attempts} split attempts. {Count} items failed permanently. Failing the translation task.",
                maxSplitAttempts, failedItems.Count);

            throw new TranslationException(
                $"Translation failed after {maxSplitAttempts} fallback attempts. {failedItems.Count} items could not be translated.");
        }
        
        _logger.LogInformation("Batch fallback completed successfully: all {Count} items translated", batch.Count);

        return results;
    }

    /// <summary>
    /// Splits a list of items into the specified number of chunks.
    /// Uses ceiling division so earlier chunks may be slightly larger for odd numbers.
    /// </summary>
    /// <param name="items">The items to split</param>
    /// <param name="splitCount">Number of chunks to create (1 = no split, 2 = halves, 3 = thirds)</param>
    /// <returns>List of chunks</returns>
    private static List<List<BatchSubtitleItem>> SplitIntoChunks(
        List<BatchSubtitleItem> items, 
        int splitCount)
    {
        // No split for level 1 - just return the full batch
        if (splitCount <= 1 || items.Count <= 1)
        {
            return new List<List<BatchSubtitleItem>> { items };
        }

        // Calculate chunk size using ceiling to handle odd numbers
        // e.g., 10 items / 3 chunks = ceil(3.33) = 4 items per chunk
        // Result: [4, 4, 2] items
        var chunkSize = (int)Math.Ceiling((double)items.Count / splitCount);
        
        // Ensure at least 1 item per chunk
        chunkSize = Math.Max(1, chunkSize);

        var chunks = new List<List<BatchSubtitleItem>>();
        for (int i = 0; i < items.Count; i += chunkSize)
        {
            var chunk = items.Skip(i).Take(chunkSize).ToList();
            chunks.Add(chunk);
        }

        return chunks;
    }
}
