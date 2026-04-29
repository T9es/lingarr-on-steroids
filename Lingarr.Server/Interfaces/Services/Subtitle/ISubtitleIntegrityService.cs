namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Service for validating subtitle integrity by comparing line counts
/// between source and target subtitles.
/// </summary>
public interface ISubtitleIntegrityService
{
    /// <summary>
    /// Validates that a target subtitle has an expected number of lines
    /// compared to the source subtitle. Used to detect partial/corrupted translations.
    /// </summary>
    /// <param name="sourceSubtitlePath">Path to the source subtitle file</param>
    /// <param name="targetSubtitlePath">Path to the target (translated) subtitle file</param>
    /// <returns>True if valid (or validation disabled); false if target appears corrupt/partial</returns>
    Task<bool> ValidateIntegrityAsync(string sourceSubtitlePath, string targetSubtitlePath);

    /// <summary>
    /// Validates subtitle integrity and returns the reason and parsed entry counts.
    /// </summary>
    /// <param name="sourceSubtitlePath">Path to the source subtitle file</param>
    /// <param name="targetSubtitlePath">Path to the target translated subtitle file</param>
    /// <returns>Detailed validation result</returns>
    Task<Models.SubtitleIntegrityCheckResult> ValidateIntegrityDetailedAsync(
        string sourceSubtitlePath,
        string targetSubtitlePath);

    /// <summary>
    /// Scans translated subtitle files for ASS/SSA artifacts.
    /// Used to detect hallucinated vector drawing garbage and damaged leaked ASS/SSA tags.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result containing list of flagged files</returns>
    Task<Models.AssVerificationResult> VerifyAssIntegrityAsync(CancellationToken ct);

    /// <summary>
    /// Validates the subtitle type by checking if the source subtitle has sufficient entries.
    /// Detects potentially incomplete subtitles like Forced or Signs-only subtitles.
    /// </summary>
    /// <param name="translationId">The translation request ID to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result containing entry count and completeness status</returns>
    Task<Models.SubtitleTypeCheckResult?> ValidateSubtitleTypeAsync(int translationId, CancellationToken ct = default);

    /// <summary>
    /// Scans all completed translations for potentially incomplete source subtitles.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summary of all incomplete subtitle findings</returns>
    Task<Models.SubtitleTypeCheckSummary> ValidateAllSubtitleTypesAsync(CancellationToken ct);
}
