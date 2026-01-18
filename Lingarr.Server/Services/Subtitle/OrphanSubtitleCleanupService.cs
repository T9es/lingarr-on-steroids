using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Service for cleaning up orphaned subtitle files when media files are upgraded.
/// Uses tag-based identification to safely remove only Lingarr-created subtitles.
/// </summary>
public class OrphanSubtitleCleanupService : IOrphanSubtitleCleanupService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ILogger<OrphanSubtitleCleanupService> _logger;

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub" };

    public OrphanSubtitleCleanupService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ILogger<OrphanSubtitleCleanupService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> CleanupOrphansAsync(string directoryPath, string oldFileName, string newFileName)
    {
        // Check if cleanup is enabled
        var cleanupEnabled = await _settingService.GetSetting(SettingKeys.Translation.CleanupOrphanedSubtitles);
        if (cleanupEnabled != "true")
        {
            _logger.LogDebug("Orphan subtitle cleanup is disabled, skipping.");
            return 0;
        }

        // Check if tagging is enabled - required for safe identification
        var taggingEnabled = await _settingService.GetSetting(SettingKeys.Translation.UseSubtitleTagging);
        if (taggingEnabled != "true")
        {
            _logger.LogWarning(
                "Orphan subtitle cleanup is enabled but subtitle tagging is disabled. " +
                "Cannot identify Lingarr-created subtitles without tagging. Skipping cleanup.");
            return 0;
        }

        // Get the configured tag
        var subtitleTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTag);
        var shortTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTagShort);
        
        if (string.IsNullOrEmpty(subtitleTag) && string.IsNullOrEmpty(shortTag))
        {
            _logger.LogWarning("No subtitle tag configured. Cannot identify Lingarr-created subtitles. Skipping cleanup.");
            return 0;
        }

        // Validate directory exists
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogDebug("Directory does not exist or is empty: {Path}", directoryPath);
            return 0;
        }

        // Find orphaned subtitles
        var cleanedCount = 0;
        var logsToAdd = new List<SubtitleCleanupLog>();

        try
        {
            // First, find all subtitles matching the OLD filename (orphans from upgrade)
            if (oldFileName != newFileName)
            {
                var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in allFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    
                    if (!SubtitleExtensions.Contains(ext)) continue;

                    // Match old filename specifically: starts with oldFileName and followed by . or [ or - (standard Radarr/Sonarr naming)
                    // We avoid glob patterns like * to be more precise
                    if (fileName.StartsWith(oldFileName) && 
                        (fileName.Length == oldFileName.Length + ext.Length || 
                         fileName[oldFileName.Length] == '.' || 
                         fileName[oldFileName.Length] == '[' || 
                         fileName[oldFileName.Length] == '-'))
                    {
                        // Check if this file has a Lingarr tag
                        var hasTag = (!string.IsNullOrEmpty(subtitleTag) && fileName.Contains(subtitleTag)) ||
                                     (!string.IsNullOrEmpty(shortTag) && fileName.Contains(shortTag));
                        
                        if (!hasTag) continue;

                        // Double check it doesn't match the new filename
                        if (fileName.StartsWith(newFileName) && 
                            (fileName.Length == newFileName.Length + ext.Length || 
                             fileName[newFileName.Length] == '.' || 
                             fileName[newFileName.Length] == '[' || 
                             fileName[newFileName.Length] == '-'))
                        {
                            continue;
                        }

                        // This is an orphaned Lingarr-created subtitle - delete it
                        try
                        {
                            File.Delete(file);
                            cleanedCount++;

                            logsToAdd.Add(new SubtitleCleanupLog
                            {
                                FilePath = file,
                                OriginalMediaFileName = oldFileName,
                                NewMediaFileName = newFileName,
                                Reason = "media_filename_changed",
                                DeletedAt = DateTime.UtcNow
                            });

                            _logger.LogInformation(
                                "Deleted orphaned subtitle: {FileName} (media changed from '{OldName}' to '{NewName}')",
                                fileName, oldFileName, newFileName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete orphaned subtitle: {Path}", file);
                        }
                    }
                }
            }

            // Remove clean slate logic for same-filename mtime changes - MediaSubtitleProcessor handles it via hashing
            // and only re-translates if necessary. Aggressive wipe here is dangerous.

            // Save cleanup logs to database
            if (logsToAdd.Count > 0)
            {
                _dbContext.SubtitleCleanupLogs.AddRange(logsToAdd);
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during orphan subtitle cleanup in directory: {Path}", directoryPath);
        }

        if (cleanedCount > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} orphaned subtitle(s) in {Directory}",
                cleanedCount, directoryPath);
        }

        return cleanedCount;
    }

    /// <inheritdoc />
    public async Task<int> CleanupStaleSubtitlesAsync(string directoryPath, string fileName, string? targetLanguage = null)
    {
        // Check if tagging is enabled - required for safe identification
        var taggingEnabled = await _settingService.GetSetting(SettingKeys.Translation.UseSubtitleTagging);
        if (taggingEnabled != "true")
        {
            _logger.LogWarning(
                "Stale subtitle cleanup requested but subtitle tagging is disabled. " +
                "Cannot identify Lingarr-created subtitles without tagging. Skipping cleanup.");
            return 0;
        }

        // Get the configured tag
        var subtitleTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTag);
        var shortTag = await _settingService.GetSetting(SettingKeys.Translation.SubtitleTagShort);

        if (string.IsNullOrEmpty(subtitleTag) && string.IsNullOrEmpty(shortTag))
        {
            _logger.LogWarning("No subtitle tag configured. Cannot identify Lingarr-created subtitles. Skipping cleanup.");
            return 0;
        }

        // Validate directory exists
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogDebug("Directory does not exist or is empty: {Path}", directoryPath);
            return 0;
        }

        var cleanedCount = 0;
        var logsToAdd = new List<SubtitleCleanupLog>();

        try
        {
            var allFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in allFiles)
            {
                var currentFileName = Path.GetFileName(file);
                var ext = Path.GetExtension(file).ToLowerInvariant();

                if (!SubtitleExtensions.Contains(ext)) continue;

                // Match filename specifically: starts with fileName and followed by . or [ or -
                if (currentFileName.StartsWith(fileName) &&
                    (currentFileName.Length == fileName.Length + ext.Length ||
                     currentFileName[fileName.Length] == '.' ||
                     currentFileName[fileName.Length] == '[' ||
                     currentFileName[fileName.Length] == '-'))
                {
                    // Check if this file has a Lingarr tag
                    var hasTag = (!string.IsNullOrEmpty(subtitleTag) && currentFileName.Contains(subtitleTag)) ||
                                 (!string.IsNullOrEmpty(shortTag) && currentFileName.Contains(shortTag));

                    if (!hasTag) continue;

                    // If targetLanguage is specified, only remove if it matches
                    if (!string.IsNullOrEmpty(targetLanguage))
                    {
                        // We look for .[targetLanguage]. in the filename
                        // This is a bit naive but matches how CreateFilePathInternal works
                        if (!currentFileName.Contains($".{targetLanguage.ToLowerInvariant()}."))
                        {
                            continue;
                        }
                    }

                    // This is a stale Lingarr-created subtitle - delete it
                    try
                    {
                        File.Delete(file);
                        cleanedCount++;

                        logsToAdd.Add(new SubtitleCleanupLog
                        {
                            FilePath = file,
                            OriginalMediaFileName = fileName,
                            NewMediaFileName = fileName,
                            Reason = string.IsNullOrEmpty(targetLanguage) ? "media_refreshed" : "retranslation_started",
                            DeletedAt = DateTime.UtcNow
                        });

                        _logger.LogInformation(
                            "Deleted stale subtitle: {FileName} (Reason: {Reason})",
                            currentFileName, string.IsNullOrEmpty(targetLanguage) ? "media refreshed" : $"re-translation for {targetLanguage}");

                        // Also remove database records for this stale subtitle
                        var staleRequests = await _dbContext.TranslationRequests
                            .Where(tr => tr.MediaId != null &&
                                         tr.SubtitleToTranslate != null &&
                                         tr.SubtitleToTranslate.Contains(fileName) &&
                                         (string.IsNullOrEmpty(targetLanguage) || tr.TargetLanguage == targetLanguage) &&
                                         (tr.Status == TranslationStatus.Completed || tr.Status == TranslationStatus.Failed))
                            .ToListAsync();

                        if (staleRequests.Any())
                        {
                            _dbContext.TranslationRequests.RemoveRange(staleRequests);
                            _logger.LogDebug("Removed {Count} stale translation request records from database", staleRequests.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete stale subtitle: {Path}", file);
                    }
                }
            }

            // Save cleanup logs to database
            if (logsToAdd.Count > 0)
            {
                _dbContext.SubtitleCleanupLogs.AddRange(logsToAdd);
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stale subtitle cleanup in directory: {Path}", directoryPath);
        }

        return cleanedCount;
    }
}
