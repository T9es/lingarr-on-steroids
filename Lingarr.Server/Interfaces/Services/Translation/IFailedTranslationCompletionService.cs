using Lingarr.Core.Entities;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface IFailedTranslationCompletionService
{
    Task<FailedTranslationCompletionResult> CompleteAsync(
        TranslationRequest request,
        IReadOnlyDictionary<int, string> edits,
        IReadOnlySet<int> sourceTextPositions,
        string logMessage,
        CancellationToken cancellationToken);

    Task<FailedTranslationCompletionResult> PublishCompletedEditsAsync(
        TranslationRequest request,
        string sourcePath,
        IReadOnlyList<SubtitleItem> translatedSubtitles,
        CancellationToken cancellationToken);
}
