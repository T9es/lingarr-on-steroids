using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationCheckpointService
{
    Task<TranslationCheckpoint?> LoadAsync(
        int translationRequestId,
        string sourceFingerprint,
        CancellationToken cancellationToken);

    Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken);

    Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken);
}
