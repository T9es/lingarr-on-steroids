using Lingarr.Core.Entities;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISourceSubtitleResolver
{
    Task<string?> ResolveReadableSourcePathAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}
