using Lingarr.Core.Entities;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleSourceSelectionService
{
    Task<SubtitleSourceSelectionResult> SelectPrimaryAsync(
        IReadOnlyCollection<EmbeddedSubtitle> candidates,
        IReadOnlyList<string> configuredSourceLanguages,
        bool allowCaptionFallback,
        CancellationToken cancellationToken = default);
}
