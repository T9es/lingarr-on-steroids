namespace Lingarr.Server.Models.Api;

/// <summary>
/// Parsed episode query pattern for fuzzy search.
/// </summary>
public class EpisodeQueryPattern
{
    /// <summary>
    /// Show title extracted from query.
    /// </summary>
    public required string ShowTitle { get; set; }

    /// <summary>
    /// Season number if specified (e.g., S01 or 1x02).
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Episode number if specified (e.g., E04 or e4).
    /// </summary>
    public int? EpisodeNumber { get; set; }
}
