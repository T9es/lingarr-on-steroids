using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Service for validating subtitle integrity by comparing line counts
/// between source and target subtitles to detect partial/corrupted translations.
/// </summary>
public class SubtitleIntegrityService : ISubtitleIntegrityService
{
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<SubtitleIntegrityService> _logger;

    /// <summary>
    /// Tolerance percentage for line count comparison.
    /// Target can have up to this percentage fewer lines than source and still be valid.
    /// </summary>
    private const double TolerancePercentage = 0.05; // 5%

    public SubtitleIntegrityService(
        ISettingService settingService,
        ISubtitleService subtitleService,
        ILogger<SubtitleIntegrityService> logger)
    {
        _settingService = settingService;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateIntegrityAsync(string sourceSubtitlePath, string targetSubtitlePath)
    {
        // Check if integrity validation is enabled
        var enabled = await _settingService.GetSetting(SettingKeys.SubtitleValidation.IntegrityValidationEnabled);
        if (enabled != "true")
        {
            _logger.LogInformation("Integrity validation is disabled (setting={Setting}), skipping check for {TargetPath}", enabled ?? "null", targetSubtitlePath);
            return true; // Validation disabled, treat as valid
        }

        // Validate file existence
        if (!File.Exists(sourceSubtitlePath))
        {
            _logger.LogWarning("Source subtitle not found for integrity check: {Path}", sourceSubtitlePath);
            return true; // Can't validate without source
        }

        if (!File.Exists(targetSubtitlePath))
        {
            _logger.LogInformation("Target subtitle not found for integrity check: {Path}", targetSubtitlePath);
            return true; // No target to validate
        }

        try
        {
            // Parse both subtitle files
            var sourceSubtitles = await _subtitleService.ReadSubtitles(sourceSubtitlePath);
            var targetSubtitles = await _subtitleService.ReadSubtitles(targetSubtitlePath);

            var sourceCount = sourceSubtitles.Count;
            var targetCount = targetSubtitles.Count;

            if (sourceCount == 0)
            {
                _logger.LogInformation("Source subtitle has no lines, skipping integrity check");
                return true;
            }

            // Calculate minimum acceptable line count (with tolerance)
            var minimumAcceptable = (int)(sourceCount * (1 - TolerancePercentage));

            if (targetCount < minimumAcceptable)
            {
                _logger.LogWarning(
                    "Subtitle integrity check FAILED: Target has {TargetCount} lines but source has {SourceCount} (minimum acceptable: {Minimum}). " +
                    "File may be corrupted/partial: {TargetPath}",
                    targetCount, sourceCount, minimumAcceptable, targetSubtitlePath);
                return false;
            }

            _logger.LogInformation(
                "Subtitle integrity check PASSED: {TargetCount}/{SourceCount} lines in {Path}",
                targetCount, sourceCount, targetSubtitlePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during subtitle integrity check for {TargetPath}", targetSubtitlePath);
            // On error, don't block processing - return true
            return true;
        }
    }
}
