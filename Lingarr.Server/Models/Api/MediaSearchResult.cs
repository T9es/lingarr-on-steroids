using Lingarr.Core.Enum;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Search result for test translations with hierarchical show→season→episode structure.
/// </summary>
public class MediaSearchResult
{
    /// <summary>
    /// Movies matching the search query.
    /// </summary>
    public List<MovieSearchResult> Movies { get; set; } = [];

    /// <summary>
    /// Shows matching the search query with their seasons and episodes.
    /// </summary>
    public List<ShowSearchResult> Shows { get; set; } = [];
}

/// <summary>
/// Movie search result with subtitle information.
/// </summary>
public class MovieSearchResult
{
    /// <summary>
    /// Movie title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Movie ID.
    /// </summary>
    public int MovieId { get; set; }

    /// <summary>
    /// Path to the poster image.
    /// </summary>
    public string? PosterPath { get; set; }

    /// <summary>
    /// Release year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Available subtitle files.
    /// </summary>
    public List<SubtitleInfo> Subtitles { get; set; } = [];

    /// <summary>
    /// Embedded subtitles from the media file.
    /// </summary>
    public List<EmbeddedSubtitleInfo> EmbeddedSubtitles { get; set; } = [];
}

/// <summary>
/// Show search result with seasons and episodes.
/// </summary>
public class ShowSearchResult
{
    /// <summary>
    /// Show title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Show ID.
    /// </summary>
    public int ShowId { get; set; }

    /// <summary>
    /// Path to the poster image.
    /// </summary>
    public string? PosterPath { get; set; }

    /// <summary>
    /// Show start year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Seasons containing episodes with subtitles.
    /// </summary>
    public List<SeasonPreview> Seasons { get; set; } = [];
}

/// <summary>
/// Season preview with episodes.
/// </summary>
public class SeasonPreview
{
    /// <summary>
    /// Season number (0 for specials).
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Episodes in this season.
    /// </summary>
    public List<EpisodePreview> Episodes { get; set; } = [];
}

/// <summary>
/// Episode preview with subtitle information.
/// </summary>
public class EpisodePreview
{
    /// <summary>
    /// Episode ID.
    /// </summary>
    public int EpisodeId { get; set; }

    /// <summary>
    /// Episode number.
    /// </summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Episode title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Display title (SxxEyy format).
    /// </summary>
    public string DisplayTitle => $"S{SeasonNumber:D2}E{EpisodeNumber:D2}";

    /// <summary>
    /// Season number (for display).
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Available subtitle files.
    /// </summary>
    public List<SubtitleInfo> Subtitles { get; set; } = [];

    /// <summary>
    /// Embedded subtitles from the media file.
    /// </summary>
    public List<EmbeddedSubtitleInfo> EmbeddedSubtitles { get; set; } = [];
}

/// <summary>
/// Subtitle file information.
/// </summary>
public class SubtitleInfo
{
    /// <summary>
    /// Full path to the subtitle file.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "pl").
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// File name.
    /// </summary>
    public string? FileName { get; set; }
}
