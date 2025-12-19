using Lingarr.Server.Models.Batch;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface IBatchTranslationService
{
    /// <summary>
    /// Translates a batch of subtitle items in a single request
    /// </summary>
    /// <param name="subtitleBatch">List of subtitle items to translate</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="preContext">Optional context lines before the batch (wrapper context)</param>
    /// <param name="postContext">Optional context lines after the batch (wrapper context)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        List<string>? preContext,
        List<string>? postContext,
        CancellationToken cancellationToken);
}