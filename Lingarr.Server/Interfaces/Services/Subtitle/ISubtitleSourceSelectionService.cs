using Lingarr.Core.Entities;
using Lingarr.Server.Services.Subtitle;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleSourceSelectionService
{
    /// <summary>
    /// Selects the best primary source subtitle from candidates.
    /// When <paramref name="targetLanguages"/> is provided (auto mode), configured
    /// source language filtering is bypassed and translation quality scoring is used
    /// to evaluate candidates.
    /// </summary>
    Task<SubtitleSourceSelectionResult> SelectPrimaryAsync(
        IReadOnlyCollection<EmbeddedSubtitle> candidates,
        IReadOnlyList<string> configuredSourceLanguages,
        bool allowCaptionFallback,
        IReadOnlyList<string>? targetLanguages = null,
        CancellationToken cancellationToken = default);
}
