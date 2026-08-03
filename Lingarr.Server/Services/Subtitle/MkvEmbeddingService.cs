using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class MkvEmbeddingService : IMkvEmbeddingService
{
    private const string MkvMergeBinary = "mkvmerge";
    private const string MkvPropEditBinary = "mkvpropedit";
    private const int Ext4MaxFilenameBytes = 255;
    private const string TempOutputPrefix = "lingarr_merged_";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PublicationLocks = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<MkvEmbeddingService> _logger;

    public MkvEmbeddingService(ILogger<MkvEmbeddingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Acquires an in-process publication lease for the supplied media and output paths.
    /// Callers hold the lease through ownership validation, filesystem publication, and
    /// the final database compare-and-set so replacement attempts cannot interleave output.
    /// </summary>
    internal static async Task<IAsyncDisposable> AcquirePublicationLeaseAsync(
        IEnumerable<string>? paths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keys = (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePublicationKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var gates = new List<SemaphoreSlim>(keys.Count);

        try
        {
            foreach (var key in keys)
            {
                var gate = PublicationLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken);
                gates.Add(gate);
            }

            return new PublicationLease(gates);
        }
        catch
        {
            for (var index = gates.Count - 1; index >= 0; index--)
            {
                gates[index].Release();
            }

            throw;
        }
    }

    private static string NormalizePublicationKey(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return $"logical-publication:{path}";
        }
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

        string? tempOutputPath = null;
        try
        {
            var targetFormat = SubtitleOutputModeHelper.NormalizeFormat(extension);
            var existingTrackIds = await GetLingarrTrackIdsToReplaceAsync(
                mkvPath,
                languageCode,
                targetFormat,
                trackName,
                ct);

            tempOutputPath = CreateTempOutputPath(mkvPath);

            var arguments = BuildMkvMergeArguments(mkvPath, subtitlePath, languageCode, trackName, isAss, tempOutputPath, existingTrackIds);

            _logger.LogInformation(
                "Embedding subtitle into MKV container. MKV: |Green|{MkvPath}|/Green|, Subtitle: {SubtitlePath}, Language: {LanguageCode}",
                mkvPath,
                subtitlePath,
                languageCode);

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

            if (tempOutputPath != null)
            {
                CleanupTempFile(tempOutputPath);
            }

            return new MkvEmbedResult(
                Success: false,
                Error: $"mkvmerge failed with exit code {result.ExitCode}: {TruncateOutput(result.Output)}");
        }
        catch (OperationCanceledException)
        {
            if (tempOutputPath != null)
            {
                CleanupTempFile(tempOutputPath);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during MKV embedding for {MkvPath}", mkvPath);
            if (tempOutputPath != null)
            {
                CleanupTempFile(tempOutputPath);
            }

            return new MkvEmbedResult(Success: false, Error: $"Exception during embedding: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<MkvEmbedResult> EmbedSubtitlesAsync(
        string mkvPath,
        IReadOnlyCollection<MkvSubtitleInput> subtitles,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mkvPath))
        {
            return new MkvEmbedResult(Success: false, Error: "MKV path is null or empty.");
        }

        if (subtitles == null || subtitles.Count == 0)
        {
            return new MkvEmbedResult(Success: false, Error: "At least one subtitle is required for batch MKV embedding.");
        }

        if (!File.Exists(mkvPath))
        {
            return new MkvEmbedResult(Success: false, Error: $"MKV file not found: {mkvPath}");
        }

        var missingSubtitle = subtitles.FirstOrDefault(subtitle =>
            string.IsNullOrEmpty(subtitle.SubtitlePath) || !File.Exists(subtitle.SubtitlePath));
        if (missingSubtitle != null)
        {
            var missingPath = missingSubtitle.SubtitlePath ?? string.Empty;
            return new MkvEmbedResult(Success: false, Error: $"Subtitle file not found: {missingPath}");
        }

        string? tempOutputPath = null;
        try
        {
            var identifyResult = await RunProcessAsync(
                MkvMergeBinary,
                new List<string> { "-J", mkvPath },
                ct);
            if (identifyResult.ExitCode != 0)
            {
                return new MkvEmbedResult(
                    Success: false,
                    Error: $"mkvmerge JSON identification failed with exit code {identifyResult.ExitCode}: " +
                           TruncateOutput(identifyResult.Output));
            }

            var existingTrackIds = subtitles
                .SelectMany(subtitle => FindLingarrTrackIdsToReplace(
                    identifyResult.Output,
                    subtitle.LanguageCode,
                    SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(subtitle.SubtitlePath)),
                    subtitle.TrackName))
                .Distinct()
                .ToList();

            tempOutputPath = CreateTempOutputPath(mkvPath);
            var arguments = BuildMkvMergeArguments(
                mkvPath,
                subtitles,
                tempOutputPath,
                existingTrackIds);

            _logger.LogInformation(
                "Embedding {SubtitleCount} subtitles into MKV container in one merge. MKV: |Green|{MkvPath}|/Green|",
                subtitles.Count,
                mkvPath);

            var result = await RunProcessAsync(MkvMergeBinary, arguments, ct);
            if (result.ExitCode != 0 && result.ExitCode != 1)
            {
                _logger.LogError(
                    "mkvmerge batch embedding failed with exit code {ExitCode}. Error: {Error}",
                    result.ExitCode,
                    TruncateOutput(result.Output));
                CleanupTempFile(tempOutputPath);
                return new MkvEmbedResult(
                    Success: false,
                    Error: $"mkvmerge batch embedding failed with exit code {result.ExitCode}: " +
                           TruncateOutput(result.Output));
            }

            _logger.LogInformation(
                "mkvmerge batch embedding completed successfully (exit code {ExitCode}). Output: {Output}",
                result.ExitCode,
                TruncateOutput(result.Output));
            if (result.ExitCode == 1)
            {
                _logger.LogWarning("mkvmerge reported warnings during batch embedding: {Warnings}", TruncateOutput(result.Output));
            }

            if (!File.Exists(tempOutputPath))
            {
                CleanupTempFile(tempOutputPath);
                return new MkvEmbedResult(
                    Success: false,
                    Error: $"mkvmerge reported success but batch output file was not found: {tempOutputPath}");
            }

            var swapResult = await SwapWithOriginalAsync(mkvPath, tempOutputPath, ct);
            if (!swapResult.Success)
            {
                CleanupTempFile(tempOutputPath);
                return new MkvEmbedResult(
                    Success: false,
                    Error: $"Batch MKV embedding could not publish the merged container: {swapResult.Error}");
            }

            var verifyResult = await VerifyTrackAsync(
                mkvPath,
                subtitles.First().LanguageCode,
                ct);
            if (!verifyResult)
            {
                _logger.LogWarning(
                    "Could not verify batch-embedded subtitle tracks in {MkvPath}. The tracks may still have been added successfully.",
                    mkvPath);
            }

            return new MkvEmbedResult(Success: true, OutputPath: mkvPath);
        }
        catch (OperationCanceledException)
        {
            if (tempOutputPath != null)
            {
                CleanupTempFile(tempOutputPath);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during batch MKV embedding for {MkvPath}", mkvPath);
            if (tempOutputPath != null)
            {
                CleanupTempFile(tempOutputPath);
            }

            return new MkvEmbedResult(Success: false, Error: $"Exception during batch embedding: {ex.Message}");
        }
    }

    internal static List<string> BuildMkvMergeArguments(
        string mkvPath,
        string subtitlePath,
        string languageCode,
        string? trackName,
        bool isAss,
        string tempOutputPath,
        IReadOnlyCollection<int> excludeTrackIds)
    {
        var args = new List<string>();
        args.Add("-o");
        args.Add(tempOutputPath);

        if (excludeTrackIds.Count > 0)
        {
            args.Add("--subtitle-tracks");
            args.Add($"!{string.Join(",", excludeTrackIds)}");
        }

        args.Add(mkvPath);
        args.Add("--language");
        args.Add($"0:{languageCode}");

        if (!string.IsNullOrEmpty(trackName))
        {
            args.Add("--track-name");
            args.Add($"0:{trackName}");
        }

        if (isAss)
        {
            args.Add("--default-track-flag");
            args.Add("0:no");
        }

        args.Add(subtitlePath);

        return args;
    }

    internal static List<string> BuildMkvMergeArguments(
        string mkvPath,
        IReadOnlyCollection<MkvSubtitleInput> subtitles,
        string tempOutputPath,
        IReadOnlyCollection<int> excludeTrackIds)
    {
        var args = new List<string>
        {
            "-o",
            tempOutputPath
        };

        if (excludeTrackIds.Count > 0)
        {
            args.Add("--subtitle-tracks");
            args.Add($"!{string.Join(",", excludeTrackIds)}");
        }

        args.Add(mkvPath);
        foreach (var subtitle in subtitles)
        {
            var extension = Path.GetExtension(subtitle.SubtitlePath).ToLowerInvariant();
            args.Add("--language");
            args.Add($"0:{subtitle.LanguageCode}");

            if (!string.IsNullOrEmpty(subtitle.TrackName))
            {
                args.Add("--track-name");
                args.Add($"0:{subtitle.TrackName}");
            }

            if (extension is ".ass" or ".ssa")
            {
                args.Add("--default-track-flag");
                args.Add("0:no");
            }

            args.Add(subtitle.SubtitlePath);
        }

        return args;
    }

    internal static IReadOnlyList<int> FindLingarrTrackIdsToReplace(
        string identifyJson,
        string targetLanguage,
        string targetFormat,
        string? trackName)
    {
        var normalizedFormat = SubtitleOutputModeHelper.NormalizeFormat(targetFormat);
        if (string.IsNullOrWhiteSpace(identifyJson) ||
            string.IsNullOrWhiteSpace(targetLanguage) ||
            string.IsNullOrWhiteSpace(normalizedFormat))
        {
            return [];
        }

        var ids = new List<int>();
        foreach (var track in ParseSubtitleTracks(identifyJson))
        {
            if (!IsLingarrOwnedTrack(track) ||
                !TrackLanguageMatches(track, targetLanguage) ||
                !string.Equals(MapMkvSubtitleFormat(track), normalizedFormat, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ids.Add(track.Id);
        }

        return ids;
    }

    private async Task<IReadOnlyList<int>> GetLingarrTrackIdsToReplaceAsync(
        string mkvPath,
        string languageCode,
        string targetFormat,
        string? trackName,
        CancellationToken ct)
    {
        var result = await RunProcessAsync(MkvMergeBinary, new List<string> { "-J", mkvPath }, ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"mkvmerge JSON identification failed with exit code {result.ExitCode}: {TruncateOutput(result.Output)}");
        }

        return FindLingarrTrackIdsToReplace(result.Output, languageCode, targetFormat, trackName);
    }

    private static IReadOnlyList<MkvSubtitleTrack> ParseSubtitleTracks(string identifyJson)
    {
        using var document = JsonDocument.Parse(identifyJson);
        if (!document.RootElement.TryGetProperty("tracks", out var tracksElement) ||
            tracksElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tracks = new List<MkvSubtitleTrack>();
        foreach (var trackElement in tracksElement.EnumerateArray())
        {
            if (!TryGetInt(trackElement, "id", out var id) ||
                !string.Equals(GetString(trackElement, "type"), "subtitles", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            trackElement.TryGetProperty("properties", out var properties);
            tracks.Add(new MkvSubtitleTrack(
                id,
                GetString(properties, "language"),
                GetString(properties, "language_ietf"),
                GetString(properties, "track_name"),
                GetString(properties, "codec_id"),
                GetString(trackElement, "codec")));
        }

        return tracks;
    }

    private static bool IsLingarrOwnedTrack(MkvSubtitleTrack track)
    {
        return ContainsLingarrMarker(track.Title);
    }

    private static bool ContainsLingarrMarker(string? value)
    {
        return value?.Contains("(Lingarr)", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool TrackLanguageMatches(MkvSubtitleTrack track, string targetLanguage)
    {
        return SubtitleLanguageHelper.LanguageMatches(track.Language, targetLanguage) ||
               SubtitleLanguageHelper.LanguageMatches(track.LanguageIetf, targetLanguage) ||
               SubtitleLanguageHelper.LanguageMatches(GetLingarrTitleLanguagePrefix(track.Title), targetLanguage);
    }

    private static string? GetLingarrTitleLanguagePrefix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var markerIndex = title.IndexOf("(Lingarr)", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var prefix = title[..markerIndex].Trim();
        return string.IsNullOrWhiteSpace(prefix) ? null : prefix;
    }

    private static string MapMkvSubtitleFormat(MkvSubtitleTrack track)
    {
        var codecValues = new[] { track.CodecId, track.Codec };
        foreach (var codecValue in codecValues)
        {
            var normalized = NormalizeCodecValue(codecValue);
            if (normalized is "s_text/utf8" or "subrip" or "subrip/srt" or "srt")
            {
                return ".srt";
            }

            if (normalized is "s_text/ass" or "ass" or "substationalpha")
            {
                return ".ass";
            }

            if (normalized is "s_text/ssa" or "ssa" or "substationalpha/ssa")
            {
                return ".ssa";
            }

            if (normalized is "s_text/webvtt" or "webvtt" or "vtt")
            {
                return ".vtt";
            }
        }

        return string.Empty;
    }

    private static string NormalizeCodecValue(string? value)
    {
        return value?.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out value);
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

            if (File.Exists(backupPath))
            {
                try
                {
                    if (File.Exists(originalPath))
                    {
                        File.Delete(originalPath);
                    }

                    File.Move(backupPath, originalPath, overwrite: true);
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
                new List<string> { mkvPath, "--dry-run" },
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
        List<string> argumentList,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.StartInfo.ArgumentList.Clear();
        foreach (var arg in argumentList)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        _logger.LogDebug("Started process: {FileName} with {Count} arguments", fileName, argumentList.Count);

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

    private sealed class PublicationLease : IAsyncDisposable
    {
        private readonly IReadOnlyList<SemaphoreSlim> _gates;
        private int _disposed;

        public PublicationLease(IReadOnlyList<SemaphoreSlim> gates)
        {
            _gates = gates;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                for (var index = _gates.Count - 1; index >= 0; index--)
                {
                    _gates[index].Release();
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record MkvSubtitleTrack(
    int Id,
    string? Language,
    string? LanguageIetf,
    string? Title,
    string? CodecId,
    string? Codec);
