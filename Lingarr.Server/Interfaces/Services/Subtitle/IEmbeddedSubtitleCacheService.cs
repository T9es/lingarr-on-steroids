using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface IEmbeddedSubtitleCacheService
{
    TimeSpan Retention { get; }

    string CacheRootPath { get; }

    string GetCachePath(int mediaId, MediaType mediaType, int streamIndex, string codecName, string? language);

    string GetOcrCachePath(int mediaId, MediaType mediaType, int streamIndex, string? language);

    bool IsManagedCachePath(string? path);

    bool IsExpired(string path);

    bool IsCurrentForSource(string cachePath, string sourceMediaPath);

    /// <summary>
    /// True when the recorded source snapshot for a managed cache path still matches the
    /// media file, without requiring the cache file itself to exist. Used to decide
    /// whether a missing OCR output can be regenerated from the same media.
    /// </summary>
    bool IsSourceSnapshotCurrent(string cachePath, string sourceMediaPath);

    void RecordSourceSnapshot(string cachePath, string sourceMediaPath);

    void Invalidate(string cachePath);

    void EnsureCacheDirectory();

    void Touch(string path);

    Task CleanupExpiredFilesAsync(CancellationToken cancellationToken = default);
}
