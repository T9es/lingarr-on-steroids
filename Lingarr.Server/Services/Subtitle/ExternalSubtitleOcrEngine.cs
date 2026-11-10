using System.Diagnostics;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class ExternalSubtitleOcrEngine : ISubtitleOcrEngine
{
    /// <summary>
    /// Default ceiling for one PGS OCR run. Historical runs finish in seconds (the slowest of
    /// 929 recorded runs took under six minutes), so half an hour is many times the observed
    /// worst case while still bounding a tool that hangs on a damaged or replaced source file.
    /// Overridable through <c>LINGARR_PGSTOSRT_TIMEOUT_MINUTES</c>.
    /// </summary>
    private const int DefaultOcrTimeoutMinutes = 30;

    private const int MaxOcrTimeoutMinutes = 24 * 60;

    /// <summary>
    /// Track probing with mkvmerge or ffprobe only reads file headers and must never outlive a
    /// job either.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<ExternalSubtitleOcrEngine> _logger;

    public ExternalSubtitleOcrEngine(ILogger<ExternalSubtitleOcrEngine> logger)
    {
        _logger = logger;
    }

    public async Task<SubtitleOcrEngineResult> ConvertAsync(
        string mediaPath,
        int subtitleStreamIndex,
        string outputPath,
        string tesseractLanguage,
        CancellationToken cancellationToken = default)
    {
        var trackId = await ResolveMatroskaTrackIdAsync(mediaPath, subtitleStreamIndex, cancellationToken);
        if (!trackId.HasValue)
        {
            return new SubtitleOcrEngineResult
            {
                Success = false,
                Error = $"Could not resolve Matroska subtitle track id for subtitle stream {subtitleStreamIndex}."
            };
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var dllPath = Environment.GetEnvironmentVariable("LINGARR_PGSTOSRT_DLL");
        if (string.IsNullOrWhiteSpace(dllPath))
        {
            dllPath = OperatingSystem.IsWindows()
                ? "PgsToSrt.dll"
                : "/opt/pgstosrt/PgsToSrt.dll";
        }

        var tessData = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (string.IsNullOrWhiteSpace(tessData))
        {
            tessData = OperatingSystem.IsWindows()
                ? "tessdata"
                : "/usr/share/tesseract-ocr/5/tessdata";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(dllPath);
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(mediaPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--track");
        startInfo.ArgumentList.Add(trackId.Value.ToString());
        startInfo.ArgumentList.Add("--tesseractlanguage");
        startInfo.ArgumentList.Add(tesseractLanguage);
        startInfo.ArgumentList.Add("--tesseractdata");
        startInfo.ArgumentList.Add(tessData);
        startInfo.ArgumentList.Add("--tesseractversion");
        startInfo.ArgumentList.Add("5");

        _logger.LogInformation(
            "Running PGS OCR for {MediaPath} subtitle stream {StreamIndex} as Matroska track {TrackId}",
            Path.GetFileName(mediaPath),
            subtitleStreamIndex,
            trackId.Value);

        try
        {
            var timeout = GetOcrTimeout();
            var result = await ChildProcessRunner.RunAsync(startInfo, timeout, cancellationToken);

            if (result.TimedOut)
            {
                _logger.LogWarning(
                    "PGS OCR timed out after {Timeout} for {MediaPath}; the process tree was terminated.",
                    timeout,
                    Path.GetFileName(mediaPath));
                return new SubtitleOcrEngineResult
                {
                    Success = false,
                    Error =
                        $"PgsToSrt did not finish within {timeout.TotalMinutes:0} minutes and was terminated. " +
                        "The source file may still be downloading or be damaged."
                };
            }

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                return new SubtitleOcrEngineResult
                {
                    Success = false,
                    Error = $"PgsToSrt exited with code {result.ExitCode}: {error.Trim()}"
                };
            }

            if (!File.Exists(outputPath))
            {
                return new SubtitleOcrEngineResult
                {
                    Success = false,
                    Error = "PgsToSrt finished but did not create an output file."
                };
            }

            return new SubtitleOcrEngineResult
            {
                Success = true,
                OutputPath = outputPath
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PGS OCR engine failed for {MediaPath}", mediaPath);
            return new SubtitleOcrEngineResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<int?> ResolveMatroskaTrackIdAsync(
        string mediaPath,
        int subtitleStreamIndex,
        CancellationToken cancellationToken)
    {
        var mkvmergeTrackId = await TryResolveTrackIdWithMkvmergeAsync(
            mediaPath,
            subtitleStreamIndex,
            cancellationToken);
        if (mkvmergeTrackId.HasValue)
        {
            return mkvmergeTrackId;
        }

        return await TryResolveTrackIdWithFfprobeAsync(mediaPath, subtitleStreamIndex, cancellationToken);
    }

    private async Task<int?> TryResolveTrackIdWithMkvmergeAsync(
        string mediaPath,
        int subtitleStreamIndex,
        CancellationToken cancellationToken)
    {
        var json = await RunProcessAsync("mkvmerge", ["-J", mediaPath], cancellationToken);
        if (!json.Success || string.IsNullOrWhiteSpace(json.Stdout))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json.Stdout);
            var subtitles = document.RootElement
                .GetProperty("tracks")
                .EnumerateArray()
                .Where(track => string.Equals(
                    track.GetProperty("type").GetString(),
                    "subtitles",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subtitleStreamIndex < 0 || subtitleStreamIndex >= subtitles.Count)
            {
                return null;
            }

            return subtitles[subtitleStreamIndex].GetProperty("id").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse mkvmerge track JSON for {MediaPath}", mediaPath);
            return null;
        }
    }

    private async Task<int?> TryResolveTrackIdWithFfprobeAsync(
        string mediaPath,
        int subtitleStreamIndex,
        CancellationToken cancellationToken)
    {
        var json = await RunProcessAsync(
            "ffprobe",
            ["-v", "error", "-select_streams", "s", "-show_entries", "stream=index", "-of", "json", mediaPath],
            cancellationToken);
        if (!json.Success || string.IsNullOrWhiteSpace(json.Stdout))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json.Stdout);
            var subtitles = document.RootElement
                .GetProperty("streams")
                .EnumerateArray()
                .ToList();
            if (subtitleStreamIndex < 0 || subtitleStreamIndex >= subtitles.Count)
            {
                return null;
            }

            return subtitles[subtitleStreamIndex].GetProperty("index").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse ffprobe stream JSON for {MediaPath}", mediaPath);
            return null;
        }
    }

    private static async Task<(bool Success, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            var result = await ChildProcessRunner.RunAsync(startInfo, ProbeTimeout, cancellationToken);
            return (result.Succeeded, result.StandardOutput, result.StandardError);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a job-level signal, not a probe failure: never swallow it.
            throw;
        }
        catch
        {
            return (false, string.Empty, string.Empty);
        }
    }

    private static TimeSpan GetOcrTimeout()
    {
        var configured = Environment.GetEnvironmentVariable("LINGARR_PGSTOSRT_TIMEOUT_MINUTES");
        var minutes = int.TryParse(configured, out var parsed) && parsed > 0
            ? parsed
            : DefaultOcrTimeoutMinutes;
        return TimeSpan.FromMinutes(Math.Min(minutes, MaxOcrTimeoutMinutes));
    }
}
