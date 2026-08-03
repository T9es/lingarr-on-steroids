using System.Security.Cryptography;
using System.Text.Json;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class EmbeddedSubtitleCacheService : IEmbeddedSubtitleCacheService
{
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private const int SourceSnapshotVersion = 1;
    private const string SourceSnapshotSuffix = ".source.json";
    private static readonly JsonSerializerOptions SourceSnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<EmbeddedSubtitleCacheService> _logger;

    public EmbeddedSubtitleCacheService(
        ILogger<EmbeddedSubtitleCacheService> logger)
        : this(logger, null, null)
    {
    }

    internal EmbeddedSubtitleCacheService(
        ILogger<EmbeddedSubtitleCacheService> logger,
        string? cacheRootPath,
        TimeSpan? retention)
    {
        _logger = logger;
        CacheRootPath = ResolveCacheRootPath(cacheRootPath);
        Retention = retention ?? DefaultRetention;
    }

    public TimeSpan Retention { get; }

    public string CacheRootPath { get; }

    public string GetCachePath(int mediaId, MediaType mediaType, int streamIndex, string codecName, string? language)
    {
        EnsureCacheDirectory();

        var extension = NormalizeExtension(codecName);
        var normalizedLanguage = string.IsNullOrWhiteSpace(language)
            ? "und"
            : language.Trim().ToLowerInvariant();
        var mediaMarker = mediaType == MediaType.Movie ? "movie" : "episode";
        var fileName = $"{mediaMarker}-{mediaId}-stream-{streamIndex}-{normalizedLanguage}{extension}";
        return Path.Combine(CacheRootPath, fileName);
    }

    public string GetOcrCachePath(int mediaId, MediaType mediaType, int streamIndex, string? language)
    {
        EnsureCacheDirectory();

        var normalizedLanguage = string.IsNullOrWhiteSpace(language)
            ? "und"
            : language.Trim().ToLowerInvariant();
        var mediaMarker = mediaType == MediaType.Movie ? "movie" : "episode";
        var fileName = $"{mediaMarker}-{mediaId}-stream-{streamIndex}-{normalizedLanguage}.ocr.srt";
        return Path.Combine(CacheRootPath, fileName);
    }

    public bool IsManagedCachePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var cacheRoot = Path.GetFullPath(CacheRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);

            return fullPath.StartsWith(
                cacheRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, cacheRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool IsExpired(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        return age > Retention;
    }

    public bool IsCurrentForSource(string cachePath, string sourceMediaPath)
    {
        if (!IsManagedCachePath(cachePath) ||
            !File.Exists(cachePath) ||
            IsExpired(cachePath) ||
            string.IsNullOrWhiteSpace(sourceMediaPath))
        {
            return false;
        }

        try
        {
            var sourceInfo = new FileInfo(sourceMediaPath);
            if (!sourceInfo.Exists)
            {
                return false;
            }

            var snapshotPath = GetSourceSnapshotPath(cachePath);
            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            var snapshot = JsonSerializer.Deserialize<SourceSnapshotMetadata>(
                File.ReadAllText(snapshotPath),
                SourceSnapshotJsonOptions);
            if (snapshot == null ||
                snapshot.Version != SourceSnapshotVersion ||
                string.IsNullOrWhiteSpace(snapshot.SourcePath) ||
                string.IsNullOrWhiteSpace(snapshot.ContentHash))
            {
                return false;
            }

            if (!PathsEqual(snapshot.SourcePath, sourceInfo.FullName))
            {
                return false;
            }

            if (snapshot.Length != sourceInfo.Length ||
                snapshot.LastWriteUtcTicks != sourceInfo.LastWriteTimeUtc.Ticks ||
                snapshot.CreationUtcTicks != sourceInfo.CreationTimeUtc.Ticks)
            {
                return false;
            }

            var currentContentHash = ComputeContentHash(sourceMediaPath);
            return string.Equals(
                currentContentHash,
                snapshot.ContentHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to validate embedded subtitle cache source snapshot for {CachePath}",
                cachePath);
            return false;
        }
    }

    public void RecordSourceSnapshot(string cachePath, string sourceMediaPath)
    {
        if (!IsManagedCachePath(cachePath) ||
            !File.Exists(cachePath) ||
            string.IsNullOrWhiteSpace(sourceMediaPath) ||
            !File.Exists(sourceMediaPath))
        {
            return;
        }

        string? temporaryPath = null;
        try
        {
            var snapshot = CreateSourceSnapshot(sourceMediaPath);
            var snapshotPath = GetSourceSnapshotPath(cachePath);
            temporaryPath = $"{snapshotPath}.{Guid.NewGuid():N}.tmp";
            var serialized = JsonSerializer.Serialize(snapshot, SourceSnapshotJsonOptions);

            File.WriteAllText(temporaryPath, serialized);
            File.Move(temporaryPath, snapshotPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record embedded subtitle cache source snapshot for {CachePath}",
                cachePath);
        }
        finally
        {
            if (temporaryPath != null)
            {
                TryDelete(temporaryPath);
            }
        }
    }

    public void Invalidate(string cachePath)
    {
        if (!IsManagedCachePath(cachePath))
        {
            return;
        }

        TryDelete(cachePath);
        TryDelete(GetSourceSnapshotPath(cachePath));
    }

    public void EnsureCacheDirectory()
    {
        Directory.CreateDirectory(CacheRootPath);
    }

    public void Touch(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            var snapshotPath = GetSourceSnapshotPath(path);
            if (File.Exists(snapshotPath))
            {
                File.SetLastWriteTimeUtc(snapshotPath, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh cached subtitle timestamp for {Path}", path);
        }
    }

    public Task CleanupExpiredFilesAsync(CancellationToken cancellationToken = default)
    {
        EnsureCacheDirectory();
        foreach (var filePath in Directory.EnumerateFiles(CacheRootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (filePath.EndsWith(SourceSnapshotSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var cachePath = filePath[..^SourceSnapshotSuffix.Length];
                    if (!File.Exists(cachePath) || IsExpired(cachePath))
                    {
                        File.Delete(filePath);
                    }

                    continue;
                }

                if (!IsExpired(filePath))
                {
                    continue;
                }

                File.Delete(filePath);
                TryDelete(GetSourceSnapshotPath(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired embedded subtitle cache file {Path}", filePath);
            }
        }

        return Task.CompletedTask;
    }

    private static string ResolveCacheRootPath(string? cacheRootPath)
    {
        if (!string.IsNullOrWhiteSpace(cacheRootPath))
        {
            return Path.GetFullPath(cacheRootPath);
        }

        if (!OperatingSystem.IsWindows())
        {
            return "/app/config/embedded-subtitle-cache";
        }

        return Path.Combine(AppContext.BaseDirectory, "config", "embedded-subtitle-cache");
    }

    private static string NormalizeExtension(string codecName)
    {
        return codecName.ToLowerInvariant() switch
        {
            "ass" => ".ass",
            "ssa" => ".ssa",
            "srt" => ".srt",
            "subrip" => ".srt",
            "webvtt" => ".vtt",
            "vtt" => ".vtt",
            "mov_text" => ".srt",
            "text" => ".srt",
            _ => ".srt"
        };
    }

    private static string GetSourceSnapshotPath(string cachePath)
    {
        return cachePath + SourceSnapshotSuffix;
    }

    private static SourceSnapshotMetadata CreateSourceSnapshot(string sourceMediaPath)
    {
        var sourceInfo = new FileInfo(sourceMediaPath);
        var contentHash = ComputeContentHash(sourceMediaPath);

        return new SourceSnapshotMetadata
        {
            Version = SourceSnapshotVersion,
            SourcePath = sourceInfo.FullName,
            Length = sourceInfo.Length,
            LastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
            CreationUtcTicks = sourceInfo.CreationTimeUtc.Ticks,
            ContentHash = contentHash
        };
    }

    private static string ComputeContentHash(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool PathsEqual(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class SourceSnapshotMetadata
    {
        public int Version { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public long Length { get; init; }
        public long LastWriteUtcTicks { get; init; }
        public long CreationUtcTicks { get; init; }
        public string ContentHash { get; init; } = string.Empty;
    }

}
