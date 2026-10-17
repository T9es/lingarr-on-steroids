using Lingarr.Core.Entities;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISourceSubtitleResolver
{
    /// <summary>
    /// Resolves and validates the source subtitle used by a translation execution.
    /// </summary>
    Task<PreparedTranslationSource?> PrepareTranslationSourceAsync(
        TranslationRequest request,
        TranslationSourcePreparationOptions options,
        CancellationToken cancellationToken = default);

    Task<string?> ResolveReadableSourcePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}
