using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/blocked-media")]
public class BlockedMediaController : ControllerBase
{
    private readonly IBlockedMediaService _blockedMediaService;

    public BlockedMediaController(IBlockedMediaService blockedMediaService)
    {
        _blockedMediaService = blockedMediaService;
    }

    /// <summary>
    /// Retrieves one page of media items currently blocked from translation:
    /// failed translations (Failed), OCR quality-blocked (OcrBlocked), needing
    /// re-analysis (Stale) and waiting for a source subtitle (AwaitingSource).
    /// Ordered by state (OcrBlocked, Failed, Stale, AwaitingSource) then title.
    /// </summary>
    /// <param name="page">One-based page number (default 1).</param>
    /// <param name="pageSize">Items per page (default 50, maximum 500).</param>
    /// <param name="state">Optional state filter; omit for all blocked states.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with the paged blocked media items and per-state counts.</returns>
    [HttpGet]
    public async Task<ActionResult<BlockedMediaPageResponse>> GetBlockedMedia(
        int page = 1,
        int pageSize = 50,
        TranslationState? state = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _blockedMediaService.GetBlockedMediaAsync(
            page,
            pageSize,
            state,
            cancellationToken);
        return Ok(items);
    }

    /// <summary>
    /// Explicitly re-evaluates a blocked movie or episode. This is the only path
    /// that refreshes a row from the tab; reads never mutate state.
    /// </summary>
    /// <param name="mediaType">"movie" or "episode".</param>
    /// <param name="mediaId">Id of the media item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with the recomputed state, or 404 when the item does not exist.</returns>
    [HttpPost("{mediaType}/{mediaId:int}/recheck")]
    public async Task<ActionResult<BlockedMediaRecheckResponse>> RecheckMedia(
        string mediaType,
        int mediaId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var parsedMediaType) ||
            parsedMediaType is not (MediaType.Movie or MediaType.Episode))
        {
            return BadRequest(new { Error = "mediaType must be 'movie' or 'episode'." });
        }

        var result = await _blockedMediaService.RecheckMediaAsync(
            parsedMediaType,
            mediaId,
            cancellationToken);

        return result == null ? NotFound() : Ok(result);
    }
}
