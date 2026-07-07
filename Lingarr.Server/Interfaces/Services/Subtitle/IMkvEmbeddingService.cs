namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface IMkvEmbeddingService
{
    /// <summary>
    /// Checks if a file path's filename component exceeds the ext4 255-byte limit.
    /// </summary>
    bool WouldExceedPathLimit(string filePath);

    /// <summary>
    /// Embeds a subtitle file into an MKV container using mkvmerge.
    /// Returns the path to the new MKV file (or original if merged in-place).
    /// </summary>
    Task<MkvEmbedResult> EmbedSubtitleAsync(
        string mkvPath,
        string subtitlePath,
        string languageCode,
        string? trackName = null,
        CancellationToken ct = default);
}

public record MkvEmbedResult(bool Success, string? OutputPath = null, string? Error = null);