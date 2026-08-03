using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationCheckpointService
{
    Task<TranslationCheckpoint?> LoadAsync(
        int translationRequestId,
        string sourceFingerprint,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads a checkpoint by request ID without fingerprint validation.
    /// Used by the compare controller to read partial translations for failed requests.
    /// </summary>
    Task<TranslationCheckpoint?> LoadByRequestIdAsync(
        int translationRequestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a full checkpoint to disk. Used by the compare controller when
    /// the user edits translations for a failed request.
    /// </summary>
    Task SaveCheckpointAsync(
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task SaveCheckpointAsync(
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken,
        string? ownershipToken);

    Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken);

    Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken,
        string? ownershipToken);

    Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken);

    Task DeleteAsync(
        int translationRequestId,
        CancellationToken cancellationToken,
        string? ownershipToken)
        => DeleteAsync(translationRequestId, cancellationToken);
}
