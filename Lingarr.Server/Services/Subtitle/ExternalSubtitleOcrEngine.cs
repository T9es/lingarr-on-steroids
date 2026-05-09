using System.Diagnostics;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class ExternalSubtitleOcrEngine : ISubtitleOcrEngine
{
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

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add(dllPath);
        process.StartInfo.ArgumentList.Add("--input");
        process.StartInfo.ArgumentList.Add(mediaPath);
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add("--track");
        process.StartInfo.ArgumentList.Add(trackId.Value.ToString());
        process.StartInfo.ArgumentList.Add("--tesseractlanguage");
        process.StartInfo.ArgumentList.Add(tesseractLanguage);
        process.StartInfo.ArgumentList.Add("--tesseractdata");
        process.StartInfo.ArgumentList.Add(tessData);
        process.StartInfo.ArgumentList.Add("--tesseractversion");
        process.StartInfo.ArgumentList.Add("5");

        _logger.LogInformation(
            "Running PGS OCR for {MediaPath} subtitle stream {StreamIndex} as Matroska track {TrackId}",
            Path.GetFileName(mediaPath),
            subtitleStreamIndex,
            trackId.Value);

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return new SubtitleOcrEngineResult
                {
                    Success = false,
                    Error = $"PgsToSrt exited with code {process.ExitCode}: {error.Trim()}"
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
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode == 0, await stdoutTask, await stderrTask);
        }
        catch
        {
            return (false, string.Empty, string.Empty);
        }
    }
}
