using Lingarr.Server.Models.Integrations;
using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleProvider
{
    string Name { get; }
    
    // Search methods
    Task<List<SubtitleSearchResult>> SearchByHashAsync(string hash, long fileSizeBytes, CancellationToken cancellationToken);
    Task<List<SubtitleSearchResult>> SearchByImdbAsync(string imdbId, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken);
    Task<List<SubtitleSearchResult>> SearchByTmdbAsync(int tmdbId, MediaType mediaType, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken);
    Task<List<SubtitleSearchResult>> SearchByQueryAsync(string query, int? seasonNumber, int? episodeNumber, CancellationToken cancellationToken);
    
    // Download
    Task<string?> DownloadSubtitleAsync(string downloadLink, CancellationToken cancellationToken);
}

public class SubtitleSearchResult
{
    public string Provider { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Format { get; set; } = "srt";
    public string DownloadLink { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? ReleaseGroup { get; set; }
    public bool IsHearingImpaired { get; set; }
}
