using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Resolves media file durations with ffprobe so subtitle density checks can scale with runtime.
/// Results are cached per path, file size and last write time in a small bounded cache.
/// Every failure returns null instead of throwing, so callers fall back to the legacy threshold.
/// </summary>
public class MediaDurationProvider : IMediaDurationProvider
{
    private const int CacheCapacity = 2048;
    private static readonly TimeSpan FfprobeTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<MediaDurationProvider> _logger;
    private readonly ConcurrentDictionary<string, double> _durationCache = new(StringComparer.Ordinal);

    public MediaDurationProvider(ILogger<MediaDurationProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<double?> GetDurationSecondsAsync(
        string mediaFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mediaFilePath))
            {
                return null;
            }

            FileInfo mediaFile;
            try
            {
                mediaFile = new FileInfo(mediaFilePath);
                if (!mediaFile.Exists)
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not inspect media file for duration: {Path}", mediaFilePath);
                return null;
            }

            var cacheKey = BuildCacheKey(mediaFile);
            if (_durationCache.TryGetValue(cacheKey, out var cachedDuration))
            {
                return cachedDuration;
            }

            var durationSeconds = await ProbeDurationAsync(mediaFilePath, cancellationToken);
            if (durationSeconds.HasValue)
            {
                StoreDuration(cacheKey, durationSeconds.Value);
            }

            return durationSeconds;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve media duration for {Path}", mediaFilePath);
            return null;
        }
    }

    private async Task<double?> ProbeDurationAsync(
        string mediaFilePath,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(mediaFilePath) };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not start ffprobe for duration probe: {Path}", mediaFilePath);
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(FfprobeTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            await IgnoreFaultsAsync(outputTask);
            await IgnoreFaultsAsync(errorTask);
            _logger.LogDebug(
                "ffprobe duration probe did not finish within {Timeout} for {Path}",
                FfprobeTimeout,
                mediaFilePath);
            return null;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            _logger.LogDebug(
                "ffprobe duration probe failed with exit code {ExitCode} for {Path}: {Error}",
                process.ExitCode,
                mediaFilePath,
                error.Trim());
            return null;
        }

        return ParseDuration(output);
    }

    private static ProcessStartInfo CreateStartInfo(string mediaFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(mediaFilePath);

        return startInfo;
    }

    private static double? ParseDuration(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) ||
            !double.TryParse(
                output.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var durationSeconds) ||
            double.IsNaN(durationSeconds) ||
            double.IsInfinity(durationSeconds) ||
            durationSeconds <= 0)
        {
            return null;
        }

        return durationSeconds;
    }

    private static string BuildCacheKey(FileInfo mediaFile)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{mediaFile.FullName}|{mediaFile.Length}|{mediaFile.LastWriteTimeUtc.Ticks}");
    }

    private void StoreDuration(string cacheKey, double durationSeconds)
    {
        if (_durationCache.Count >= CacheCapacity)
        {
            _durationCache.Clear();
        }

        _durationCache[cacheKey] = durationSeconds;
    }

    private void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not stop the timed out ffprobe duration probe");
        }
    }

    private static async Task IgnoreFaultsAsync(Task<string> readTask)
    {
        try
        {
            await readTask;
        }
        catch
        {
            // The process was killed; the read is expected to fail or return partial data.
        }
    }
}
