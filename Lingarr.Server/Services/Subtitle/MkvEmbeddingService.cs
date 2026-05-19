using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class MkvEmbeddingService : IMkvEmbeddingService
{
    private const string MkvMergeBinary = "mkvmerge";
    private const string MkvPropEditBinary = "mkvpropedit";
    private const int Ext4MaxFilenameBytes = 255;
    private const string TempOutputPrefix = "lingarr_merged_";

    private readonly ILogger<MkvEmbeddingService> _logger;

    public MkvEmbeddingService(ILogger<MkvEmbeddingService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool WouldExceedPathLimit(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        var byteCount = Encoding.UTF8.GetByteCount(fileName);
        return byteCount > Ext4MaxFilenameBytes;
    }

    public static string CreateTempOutputPath(string mkvPath)
    {
        var directory = Path.GetDirectoryName(mkvPath) ?? string.Empty;
        var extension = Path.GetExtension(mkvPath);
        return Path.Combine(directory, $"{TempOutputPrefix}{Guid.NewGuid():N}{extension}");
    }

    /// <inheritdoc />
    public async Task<MkvEmbedResult> EmbedSubtitleAsync(
        string mkvPath,
        string subtitlePath,
        string languageCode,
        string? trackName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mkvPath))
        {
            return new MkvEmbedResult(Success: false, Error: "MKV path is null or empty.");
        }

        if (string.IsNullOrEmpty(subtitlePath))
        {
            return new MkvEmbedResult(Success: false, Error: "Subtitle path is null or empty.");
        }

        if (!File.Exists(mkvPath))
        {
            return new MkvEmbedResult(Success: false, Error: $"MKV file not found: {mkvPath}");
        }

        if (!File.Exists(subtitlePath))
        {
            return new MkvEmbedResult(Success: false, Error: $"Subtitle file not found: {subtitlePath}");
        }

        var extension = Path.GetExtension(subtitlePath).ToLowerInvariant();
        var isAss = extension is ".ass" or ".ssa";

        var existingTrackIds = await GetExistingLanguageTrackIds(mkvPath, languageCode);

        var tempOutputPath = CreateTempOutputPath(mkvPath);

        var arguments = BuildMkvMergeArguments(mkvPath, subtitlePath, languageCode, trackName, isAss, tempOutputPath, existingTrackIds);

        _logger.LogInformation(
            "Embedding subtitle into MKV container. MKV: |Green|{MkvPath}|/Green|, Subtitle: {SubtitlePath}, Language: {LanguageCode}",
            mkvPath,
            subtitlePath,
            languageCode);

        try
        {
            var result = await RunProcessAsync(MkvMergeBinary, arguments, ct);

            if (result.ExitCode == 0 || result.ExitCode == 1)
            {
                _logger.LogInformation(
                    "mkvmerge completed successfully (exit code {ExitCode}). Output: {Output}",
                    result.ExitCode,
                    TruncateOutput(result.Output));

                if (result.ExitCode == 1)
                {
                    _logger.LogWarning("mkvmerge reported warnings: {Warnings}", TruncateOutput(result.Output));
                }

                if (!File.Exists(tempOutputPath))
                {
                    return new MkvEmbedResult(
                        Success: false,
                        Error: $"mkvmerge reported success but output file not found: {tempOutputPath}");
                }

                var swapResult = await SwapWithOriginalAsync(mkvPath, tempOutputPath, ct);
                if (!swapResult.Success)
                {
                    return swapResult;
                }

                var verifyResult = await VerifyTrackAsync(mkvPath, languageCode, ct);
                if (!verifyResult)
                {
                    _logger.LogWarning(
                        "Could not verify embedded subtitle track for language {LanguageCode} in {MkvPath}. " +
                        "The track may still have been added successfully.",
                        languageCode,
                        mkvPath);
                }

                return new MkvEmbedResult(Success: true, OutputPath: mkvPath);
            }

            _logger.LogError(
                "mkvmerge failed with exit code {ExitCode}. Error: {Error}",
                result.ExitCode,
                TruncateOutput(result.Output));

            CleanupTempFile(tempOutputPath);

            return new MkvEmbedResult(
                Success: false,
                Error: $"mkvmerge failed with exit code {result.ExitCode}: {TruncateOutput(result.Output)}");
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempOutputPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during MKV embedding for {MkvPath}", mkvPath);
            CleanupTempFile(tempOutputPath);
            return new MkvEmbedResult(Success: false, Error: $"Exception during embedding: {ex.Message}");
        }
    }

    private static string BuildMkvMergeArguments(
        string mkvPath,
        string subtitlePath,
        string languageCode,
        string? trackName,
        bool isAss,
        string tempOutputPath,
        List<int> excludeTrackIds)
    {
        var args = new StringBuilder();
        args.Append($"-o \"{tempOutputPath}\"");

        if (excludeTrackIds.Count > 0)
        {
            args.Append(" --subtitle-tracks");
            foreach (var id in excludeTrackIds)
            {
                args.Append($" !{id}");
            }
        }

        args.Append($" --language 0:{languageCode}");

        if (!string.IsNullOrEmpty(trackName))
        {
            args.Append($" --track-name \"0:{trackName}\"");
        }

        if (isAss)
        {
            args.Append(" --default-track-flag 0:no");
        }

        args.Append($" \"{mkvPath}\"");
        args.Append($" \"{subtitlePath}\"");

        return args.ToString();
    }

    private async Task<List<int>> GetExistingLanguageTrackIds(string mkvPath, string languageCode)
    {
        try
        {
            var result = await RunProcessAsync(MkvMergeBinary, $"-i \"{mkvPath}\"", CancellationToken.None);
            var tracks = new List<int>();
            foreach (var line in result.Output.Split('\n'))
            {
                if (line.Contains("subtitles") && line.Contains($"language: {languageCode}"))
                {
                    var match = Regex.Match(line, @"Track ID (\d+)");
                    if (match.Success)
                    {
                        tracks.Add(int.Parse(match.Groups[1].Value));
                    }
                }
            }
            return tracks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query existing tracks for {MkvPath}", mkvPath);
            return new List<int>();
        }
    }

    private async Task<MkvEmbedResult> SwapWithOriginalAsync(
        string originalPath,
        string tempPath,
        CancellationToken ct)
    {
        string? backupPath = null;
        try
        {
            ct.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(originalPath)!;
            backupPath = Path.Combine(dir, $".lingarr_swap_backup_{Guid.NewGuid():N}");
            File.Move(originalPath, backupPath, overwrite: true);
            File.Move(tempPath, originalPath, overwrite: true);
            File.Delete(backupPath);

            _logger.LogInformation("Successfully swapped merged MKV with original: {MkvPath}", originalPath);
            return new MkvEmbedResult(Success: true, OutputPath: originalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to swap merged MKV with original: {OriginalPath}", originalPath);

            if (File.Exists(backupPath) && !File.Exists(originalPath))
            {
                try
                {
                    File.Move(backupPath, originalPath);
                    _logger.LogInformation("Restored original MKV from backup: {MkvPath}", originalPath);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Failed to restore original MKV from backup: {MkvPath}", originalPath);
                }
            }

            return new MkvEmbedResult(
                Success: false,
                Error: $"Failed to swap merged file with original: {ex.Message}");
        }
    }

    private async Task<bool> VerifyTrackAsync(string mkvPath, string languageCode, CancellationToken ct)
    {
        try
        {
            var result = await RunProcessAsync(
                MkvPropEditBinary,
                $"\"{mkvPath}\" --dry-run",
                ct);

            if (result.ExitCode == 0)
            {
                _logger.LogDebug("mkvpropedit verification succeeded for {MkvPath}", mkvPath);
                return true;
            }

            _logger.LogWarning(
                "mkvpropedit verification returned exit code {ExitCode} for {MkvPath}",
                result.ExitCode,
                mkvPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mkvpropedit verification failed for {MkvPath}", mkvPath);
            return false;
        }
    }

    private async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        _logger.LogDebug("Started process: {FileName} {Arguments}", fileName, TruncateOutput(arguments, 200));

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        outputBuilder.Append(output);
        outputBuilder.Append(error);

        var combinedOutput = outputBuilder.ToString();

        return (process.ExitCode, combinedOutput);
    }

    private void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
                _logger.LogDebug("Cleaned up temporary MKV file: {TempPath}", tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary MKV file: {TempPath}", tempPath);
        }
    }

    private static string TruncateOutput(string output, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(output))
        {
            return string.Empty;
        }

        return output.Length <= maxLength ? output : output[..maxLength] + "...";
    }
}
