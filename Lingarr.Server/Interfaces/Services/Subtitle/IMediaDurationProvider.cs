namespace Lingarr.Server.Interfaces.Services.Subtitle;

/// <summary>
/// Resolves the duration of a media file so subtitle density checks can scale with runtime.
/// Implementations must never throw: any failure (missing file, missing ffprobe, timeout,
/// unparsable output) yields null so callers keep the legacy fixed threshold.
/// </summary>
public interface IMediaDurationProvider
{
    /// <summary>
    /// Returns the format duration of the media file in seconds, or null when it cannot be resolved.
    /// </summary>
    Task<double?> GetDurationSecondsAsync(
        string mediaFilePath,
        CancellationToken cancellationToken = default);
}
