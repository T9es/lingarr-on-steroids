using System.Diagnostics;
using System.Text;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class MkvEmbeddingService : IMkvEmbeddingService
{
    private const string MkvMergePath = "/usr/bin/mkvmerge";
    private const string MkvPropEditPath = "/usr/bin/mkvpropedit";
    private const int Ext4MaxFilenameBytes = 255;

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

        var directory = Path.GetDirectoryName(mkvPath) ?? string.Empty;
        var mkvFileName = Path.GetFileNameWithoutExtension(mkvPath);
        var tempOutputPath = Path.Combine(directory, $"{mkvFileName}.lingarr_merged{Path.GetExtension(mkvPath)}");

        var arguments = BuildMkvMergeArguments(mkvPath, subtitlePath, languageCode, trackName, isAss, tempOutputPath);

        _logger.LogInformation(
            "Embedding subtitle into MKV container. MKV: |Green|{MkvPath}|/Green|, Subtitle: {SubtitlePath}, Language: {LanguageCode}",
            mkvPath,
            subtitlePath,
            languageCode);

        try
        {
            var result = await RunProcessAsync(MkvMergePath, arguments, ct);

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
        string tempOutputPath)
    {
        var args = new StringBuilder();
        args.Append($"-o \"{tempOutputPath}\"");
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

    private async Task<MkvEmbedResult> SwapWithOriginalAsync(
        string originalPath,
        string tempPath,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var backupPath = originalPath + ".lingarr_backup";
            File.Move(originalPath, backupPath, overwrite: true);
            File.Move(tempPath, originalPath, overwrite: true);

            File.Delete(backupPath);

            _logger.LogInformation("Successfully swapped merged MKV with original: {MkvPath}", originalPath);
            return new MkvEmbedResult(Success: true, OutputPath: originalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to swap merged MKV with original: {OriginalPath}", originalPath);

            if (File.Exists(originalPath + ".lingarr_backup") && !File.Exists(originalPath))
            {
                try
                {
                    File.Move(originalPath + ".lingarr_backup", originalPath);
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
                MkvPropEditPath,
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