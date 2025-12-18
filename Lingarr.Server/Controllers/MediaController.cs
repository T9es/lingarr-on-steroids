using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IMediaService _mediaService;
    private readonly ITranslationRequestService _translationRequestService;

    public MediaController(IMediaService mediaService, ITranslationRequestService translationRequestService)
    {
        _mediaService = mediaService;
        _translationRequestService = translationRequestService;
    }
    
    /// <summary>
    /// Retrieves a paginated list of movies based on optional search criteria and sorting parameters.
    /// </summary>
    /// <param name="searchQuery">An optional search query to filter movies by title or other attributes.</param>
    /// <param name="orderBy">An optional parameter specifying the field to sort by (e.g., "Title", "DateAdded").</param>
    /// <param name="ascending">A boolean indicating whether to sort in ascending order (default is true).</param>
    /// <param name="pageSize">The number of movies to return per page (default is 20).</param>
    /// <param name="pageNumber">The page number to retrieve (default is 1).</param>
    /// <returns>Returns an HTTP 200 OK response with a paginated list of movies.</returns>
    [HttpGet("movies")]
    public async Task<ActionResult<PagedResult<MovieResponse>>> GetMovies(
        string? searchQuery,
        string? orderBy,
        bool ascending = true,
        int pageSize = 20,
        int pageNumber = 1)
    {
        var value = await _mediaService.GetMovies(
            searchQuery,
            orderBy,
            ascending,
            pageNumber,
            pageSize);

        return Ok(value);
    }
    
    /// <summary>
    /// Retrieves a paginated list of shows based on optional search criteria and sorting parameters.
    /// </summary>
    /// <param name="searchQuery">An optional search query to filter shows by title or other attributes.</param>
    /// <param name="orderBy">An optional parameter specifying the field to sort by (e.g., "Title", "DateAdded").</param>
    /// <param name="ascending">A boolean indicating whether to sort in ascending order (default is true).</param>
    /// <param name="pageSize">The number of shows to return per page (default is 20).</param>
    /// <param name="pageNumber">The page number to retrieve (default is 1).</param>
    /// <returns>Returns an HTTP 200 OK response with a paginated list of shows.</returns>
    [HttpGet("shows")]
    public async Task<ActionResult<PagedResult<Show>>> GetShows(
        string? searchQuery,
        string? orderBy,
        bool ascending = true,
        int pageSize = 20,
        int pageNumber = 1)
    {
        var value = await _mediaService.GetShows(
            searchQuery,
            orderBy,
            ascending,
            pageNumber,
            pageSize);
        return Ok(value);
    }
    
    /// <summary>
    /// Toggles the exclusion status of a specified media item from translation.
    /// </summary>
    /// <param name="request">The request object containing the media type and id.</param>
    [HttpPost("exclude")]
    public async Task<ActionResult<PagedResult<Show>>> Exclude([FromBody] ExcludeRequest request)
    {
        var value = await _mediaService.Exclude(request.MediaType, request.Id);
        return Ok(value);
    }
    
    /// <summary>
    /// Sets the amount of hours a media file needs to exist before translation is initiated.
    /// </summary>
    /// <param name="request">The request object containing the media type, id, and amount of hours to be set.</param>
    [HttpPost("threshold")]
    public async Task<ActionResult<PagedResult<Show>>> Threshold([FromBody] ThresholdRequest request)
    {
        var value = await _mediaService.Threshold(request.MediaType, request.Id, request.Hours);
        return Ok(value);
    }

    /// <summary>
    /// Toggles the priority status of a specified media item.
    /// </summary>
    /// <param name="request">The request object containing the media type and id.</param>
    [HttpPost("priority")]
    public async Task<ActionResult<bool>> Priority([FromBody] ExcludeRequest request)
    {
        var value = await _mediaService.TogglePriority(request.MediaType, request.Id);
        if (value)
        {
            await _translationRequestService.RefreshPriorityForMedia(request.MediaType, request.Id);
        }
        return Ok(value);
    }
    /// <summary>
    /// Triggers an integrity check for the specified media item.
    /// </summary>
    /// <param name="request">The request object containing the media type and id.</param>
    [HttpPost("integrity-check")]
    public async Task<ActionResult<int>> IntegrityCheck([FromBody] ExcludeRequest request)
    {
        var queuedCount = await _mediaService.IntegrityCheck(request.MediaType, request.Id);
        return Ok(queuedCount);
    }
    
    /// <summary>
    /// Triggers a bulk integrity check for all Complete-state media items.
    /// Returns the job ID for tracking progress.
    /// </summary>
    [HttpPost("bulk-integrity-check")]
    public ActionResult<string> BulkIntegrityCheck()
    {
        var jobId = _mediaService.StartBulkIntegrityCheck();
        return Ok(new { jobId });
    }
    
    /// <summary>
    /// Gets the current status of the bulk integrity check job.
    /// </summary>
    [HttpGet("bulk-integrity-status")]
    public ActionResult GetBulkIntegrityStatus()
    {
        var current = Jobs.BulkIntegrityStats.Current;
        if (current == null)
        {
            return Ok(new { isRunning = false });
        }
        return Ok(current);
    }
}