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
    /// Retrieves media items currently blocked from translation:
    /// OCR quality-blocked (OcrBlocked), needing re-analysis (Stale) and
    /// waiting for a source subtitle (AwaitingSource).
    /// Ordered by state (OcrBlocked, Stale, AwaitingSource) then title.
    /// </summary>
    /// <param name="limit">Maximum number of items to return (default 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with the list of blocked media items.</returns>
    [HttpGet]
    public async Task<ActionResult<List<BlockedMediaItemResponse>>> GetBlockedMedia(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var items = await _blockedMediaService.GetBlockedMediaAsync(limit, cancellationToken);
        return Ok(items);
    }
}
