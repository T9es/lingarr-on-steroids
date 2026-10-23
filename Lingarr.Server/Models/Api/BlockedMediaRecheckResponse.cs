using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Result of an explicit blocked media re-check. The state is recomputed from the
/// current media data; a row may move to a different state or leave the blocked list.
/// </summary>
public class BlockedMediaRecheckResponse
{
    /// <summary>
    /// Id of the movie or episode.
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// Media kind: "movie" or "episode".
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Translation state after re-evaluation (serialized as the enum name).
    /// </summary>
    public TranslationState State { get; set; }

    /// <summary>
    /// When the re-evaluation ran.
    /// </summary>
    public DateTime EvaluatedAt { get; set; }
}
