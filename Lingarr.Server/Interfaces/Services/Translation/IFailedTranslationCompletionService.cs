using Lingarr.Core.Entities;
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
}
