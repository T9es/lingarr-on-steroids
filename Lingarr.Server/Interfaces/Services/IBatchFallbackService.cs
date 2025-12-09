using Lingarr.Server.Models.Batch;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Provides fallback translation with graduated chunk splitting when batch translations fail.
/// </summary>
public interface IBatchFallbackService
{
    /// <summary>
    /// Translates a batch of subtitles with graduated fallback on failure.
    /// When a batch fails, it's split into progressively smaller chunks and retried.
    /// </summary>
    /// <param name="batch">The batch of subtitle items to translate</param>
    /// <param name="batchService">The batch translation service to use</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="maxSplitAttempts">Maximum number of split attempts (1=retry full, 2=split in half, 3=split in thirds)</param>
    /// <param name="fileIdentifier">Short identifier for the file being translated (for logging)</param>
    /// <param name="batchNumber">Current batch number (1-indexed)</param>
    /// <param name="totalBatches">Total number of batches for this file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping position to translated content. Failed items contain original text.</returns>
    Task<Dictionary<int, string>> TranslateWithFallbackAsync(
        List<BatchSubtitleItem> batch,
        IBatchTranslationService batchService,
        string sourceLanguage,
        string targetLanguage,
        int maxSplitAttempts,
        string fileIdentifier,
        int batchNumber,
        int totalBatches,
        CancellationToken cancellationToken);
}
