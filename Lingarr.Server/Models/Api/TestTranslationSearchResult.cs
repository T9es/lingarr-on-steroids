using Lingarr.Core.Enum;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Search result for test translations, representing a single media item
/// (movie or episode) and its available subtitle files.
/// </summary>
public class TestTranslationSearchResult
{
    /// <summary>
    /// Human-friendly title, e.g. "Movie Title" or
    /// "Show Title - S01E02 - Episode Title".
    /// </summary>
    public required string DisplayTitle { get; set; }

    /// <summary>
    /// The type of media (Movie or Episode).
    /// </summary>
    public required MediaType MediaType { get; set; }

    /// <summary>
    /// Lingarr media ID (Movie.Id or Episode.Id).
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// Available subtitle files for this media item.
    /// </summary>
    public List<Subtitles> Subtitles { get; set; } = new();
}

