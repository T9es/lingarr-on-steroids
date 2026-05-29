using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Scans completed translation requests for subtitle position misalignment.
/// Detects 1-position cascade shifts caused by models dropping bracketed
/// content (e.g., sound effects) and shifting subsequent positions.
/// </summary>
public interface ISubtitleAlignmentCheckService
{
    /// <summary>
    /// Scans recent completed translation requests for alignment issues.
    /// Uses word-count heuristics to detect when translated[N] appears to
    /// match source[N+1] better than source[N] across consecutive positions.
    /// </summary>
    /// <param name="maxRequests">Maximum number of recent requests to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary of alignment check results.</returns>
    Task<SubtitleAlignmentCheckSummary> ScanRecentCompletedTranslationsAsync(
        int maxRequests = 50,
        CancellationToken ct = default);
}
