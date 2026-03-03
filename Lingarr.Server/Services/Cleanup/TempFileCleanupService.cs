using Lingarr.Server.Interfaces.Services;
using Microsoft.Extensions.Hosting;

namespace Lingarr.Server.Services.Cleanup;

/// <summary>
/// Background service that cleans up orphaned temporary subtitle files on application startup.
/// Runs once at startup to remove any temp files left behind by crashed processes or failed extractions.
/// </summary>
public class TempFileCleanupService : IHostedService
{
    private readonly ILogger<TempFileCleanupService> _logger;

    public TempFileCleanupService(ILogger<TempFileCleanupService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        CleanupTempFiles();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup needed on shutdown
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up temporary subtitle files created by Lingarr.
    /// Targets files matching patterns: lingarr_preview_*.srt and lingarr_test_*.srt
    /// </summary>
    private void CleanupTempFiles()
    {
        try
        {
            var tempDir = Path.GetTempPath();
            _logger.LogDebug("Starting temp file cleanup in directory: {Directory}", tempDir);

            var filesDeleted = 0;
            var patterns = new[] { "lingarr_preview_*.srt", "lingarr_test_*.srt" };

            foreach (var pattern in patterns)
            {
                var matchingFiles = Directory.GetFiles(tempDir, pattern);
                
                foreach (var filePath in matchingFiles)
                {
                    try
                    {
                        File.Delete(filePath);
                        filesDeleted++;
                        _logger.LogDebug("Deleted orphaned temp file: {Path}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete orphaned temp file: {Path}", filePath);
                    }
                }
            }

            if (filesDeleted > 0)
            {
                _logger.LogInformation("Temp file cleanup completed: deleted {Count} orphaned files", filesDeleted);
            }
            else
            {
                _logger.LogDebug("Temp file cleanup completed: no orphaned files found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during temp file cleanup");
        }
    }
}
