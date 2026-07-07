using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Subtitle;

public class EmbeddedSubtitleCacheService : IEmbeddedSubtitleCacheService
{
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

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
                if (!IsExpired(filePath))
                {
                    continue;
                }

                File.Delete(filePath);
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
}
