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
}
