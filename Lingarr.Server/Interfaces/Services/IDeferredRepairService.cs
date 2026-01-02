using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Service for deferred repair of failed batch translations.
/// Collects failed items and retries them together at the end with surrounding context.
/// </summary>
public interface IDeferredRepairService
{
    /// <summary>
    /// Builds a contextual repair batch from failed items, including surrounding context lines.
    /// Adjacent failures share context to avoid duplication.
    /// </summary>
    /// <param name="failedItems">List of items that failed translation</param>
    /// <param name="allSubtitles">All subtitle items from the file (for context)</param>
    /// <param name="contextRadius">Number of lines before/after each failed item to include</param>
    /// <param name="stripSubtitleFormatting">Whether to use plaintext lines</param>
    /// <returns>A contextual repair batch with merged ranges</returns>
    ContextualRepairBatch BuildContextualRepairBatch(
        List<RepairItem> failedItems,
        List<SubtitleItem> allSubtitles,
        int contextRadius,
        bool stripSubtitleFormatting);
    
    /// <summary>
    /// Executes the repair batch translation with retry logic.
    /// </summary>
    /// <param name="repairBatch">The contextual repair batch to translate</param>
    /// <param name="batchService">The batch translation service to use</param>
    /// <param name="fallbackService">The fallback service for graduated chunk splitting</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="batchSize">Size of chunks to use for repair (collected from settings)</param>
    /// <param name="maxRetries">Maximum number of retry attempts</param>
    /// <param name="fileIdentifier">Short identifier for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping position to translated content (only for failed positions)</returns>
    Task<Dictionary<int, string>> ExecuteRepairAsync(
        ContextualRepairBatch repairBatch,
        IBatchTranslationService batchService,
        IBatchFallbackService fallbackService,
        string sourceLanguage,
        string targetLanguage,
        int batchSize,
        int maxRetries,
        string fileIdentifier,
        CancellationToken cancellationToken);
}
