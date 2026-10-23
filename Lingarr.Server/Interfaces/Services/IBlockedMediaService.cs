using Lingarr.Core.Enum;
using Lingarr.Server.Models.Api;

namespace Lingarr.Server.Interfaces.Services;

public interface IBlockedMediaService
{
    /// <summary>
    /// Retrieves one page of media items currently blocked from translation
    /// (OcrBlocked first, then Failed, then Stale, then AwaitingSource, each ordered by title).
    /// </summary>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="state">Optional state filter. Null returns all blocked states.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged result with the total count and per-state counts.</returns>
    Task<BlockedMediaPageResponse> GetBlockedMediaAsync(
        int page = 1,
        int pageSize = 50,
        TranslationState? state = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly re-evaluates the translation state of a blocked movie or episode.
    /// Used by the blocked media tab re-check action; this is a POST action so reads
    /// never mutate state.
    /// </summary>
    /// <param name="mediaType">Movie or episode.</param>
    /// <param name="mediaId">Id of the media item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recomputed state, or null when the media item does not exist.</returns>
    Task<BlockedMediaRecheckResponse?> RecheckMediaAsync(
        MediaType mediaType,
        int mediaId,
        CancellationToken cancellationToken = default);
}
