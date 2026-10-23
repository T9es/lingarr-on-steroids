using Lingarr.Server.Models;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Paged response for the blocked media tab. Carries the total number of matching
/// items and per-state counts so the UI can render filters without paging guesses.
/// </summary>
public class BlockedMediaPageResponse : PagedResult<BlockedMediaItemResponse>
{
    /// <summary>
    /// Number of blocked media items per <see cref="Core.Enum.TranslationState"/>
    /// name, across all pages and ignoring the state filter.
    /// </summary>
    public Dictionary<string, int> CountsByState { get; set; } = new(StringComparer.Ordinal);
}
