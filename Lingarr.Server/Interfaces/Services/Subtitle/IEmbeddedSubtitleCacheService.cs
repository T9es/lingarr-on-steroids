using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface IEmbeddedSubtitleCacheService
{
    TimeSpan Retention { get; }

    string CacheRootPath { get; }

    string GetCachePath(int mediaId, MediaType mediaType, int streamIndex, string codecName, string? language);

    bool IsManagedCachePath(string? path);

    bool IsExpired(string path);

    void EnsureCacheDirectory();

    void Touch(string path);

    Task CleanupExpiredFilesAsync(CancellationToken cancellationToken = default);
}
