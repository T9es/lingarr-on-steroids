using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslationRequestController : ControllerBase
{
    private readonly ITranslationRequestService _translationRequestService;

    public TranslationRequestController(
        ITranslationRequestService translationRequestService)
    {
        _translationRequestService = translationRequestService;
    }

    /// <summary>
    /// Gets the count of active translation requests
    /// </summary>
    /// <response code="200">Returns the count of active translation requests</response>
    /// <response code="500">If there was an error checking for updates</response>
    /// <returns>ActionResult containing the count of active translation requests</returns>
    [HttpGet("active")]
    public async Task<ActionResult<int>> GetActiveTranslationCount()
    {
        var activeCount = await _translationRequestService.GetActiveCount();
        return Ok(activeCount);
    }

    /// <summary>
    /// Gets all translation requests with Failed status
    /// </summary>
    /// <response code="200">Returns all failed translation requests</response>
    /// <response code="500">If there was an error retrieving failed requests</response>
    /// <returns>ActionResult containing the list of failed translation requests</returns>
    [HttpGet("failed")]
    public async Task<ActionResult<List<TranslationRequest>>> GetFailedRequests()
    {
        var requests = await _translationRequestService.GetFailedRequests();
        return Ok(requests);
    }

    /// <summary>
    /// Gets all translation requests with InProgress status
    /// </summary>
    /// <response code="200">Returns all in-progress translation requests</response>
    /// <response code="500">If there was an error retrieving in-progress requests</response>
    /// <returns>ActionResult containing the list of in-progress translation requests</returns>
    [HttpGet("inprogress")]
    public async Task<ActionResult<List<TranslationRequest>>> GetInProgressRequests()
    {
        var requests = await _translationRequestService.GetInProgressRequests();
        return Ok(requests);
    }

    /// <summary>
    /// Retrieves a paginated list of translation requests with optional filtering and sorting
    /// </summary>
    /// <param name="searchQuery">Optional search term to filter requests</param>
    /// <param name="orderBy">Property name to sort the results by</param>
    /// <param name="ascending">Sort direction; true for ascending, false for descending</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="pageNumber">Page number to retrieve</param>
    /// <response code="200">Returns the paginated list of translation requests</response>
    /// <response code="500">If there was an error checking for updates</response>
    /// <returns>ActionResult containing the paginated list of translation requests</returns>
    [HttpGet("requests")]
    public async Task<ActionResult<PagedResult<TranslationRequest>>> GetTranslationRequests(
        string? searchQuery,
        string? orderBy,
        bool ascending = true,
        int pageSize = 20,
        int pageNumber = 1)
    {
        var value = await _translationRequestService.GetTranslationRequests(
            searchQuery,
            orderBy,
            ascending,
            pageNumber,
            pageSize);

        return Ok(value);
    }

    /// <summary>
    /// Retrieves all persisted log entries for a specific translation request.
    /// </summary>
    /// <param name="requestId">The ID of the translation request</param>
    /// <response code="200">Returns the list of log entries (may be empty)</response>
    /// <response code="500">If there was an error while retrieving logs</response>
    [HttpGet("logs/{requestId:int}")]
    public async Task<ActionResult<List<TranslationRequestLogDto>>> GetTranslationRequestLogs(int requestId)
    {
        var logs = await _translationRequestService.GetLogsAsync(requestId);

        var response = logs
            .OrderBy(log => log.CreatedAt)
            .Select(log => new TranslationRequestLogDto
            {
                Id = log.Id,
                Level = log.Level,
                Message = log.Message,
                Details = log.Details,
                CreatedAt = log.CreatedAt
            })
            .ToList();

        return Ok(response);
    }

    /// <summary>
    /// Cancels an existing translation request
    /// </summary>
    /// <param name="cancelRequest">The translation request to cancel</param>
    /// <response code="200">Returns the canceled translation request</response>
    /// <response code="404">If the translation request was not found</response>
    /// <response code="500">If there was an error checking for updates</response>
    /// <returns>ActionResult containing the canceled translation request if found, or NotFound if the request doesn't exist</returns>
    [HttpPost("cancel")]
    public async Task<ActionResult<string>> CancelTranslationRequest([FromBody] TranslationRequest cancelRequest)
    {
        var translationRequest = await _translationRequestService.CancelTranslationRequest(cancelRequest);
        if (translationRequest != null)
        {
            return Ok(translationRequest);
        }

        return NotFound(translationRequest);
    }

    /// <summary>
    /// Removes an existing translation request
    /// </summary>
    /// <param name="cancelRequest">The translation request to remove</param>
    /// <response code="200">Returns the removed translation request</response>
    /// <response code="404">If the translation request was not found</response>
    /// <response code="500">If there was an error checking for updates</response>
    /// <returns>ActionResult containing the removed translation request if found, or NotFound if the request doesn't exist</returns>
    [HttpPost("remove")]
    public async Task<ActionResult<string>> RemoveTranslationRequest([FromBody] TranslationRequest cancelRequest)
    {
        var translationRequest = await _translationRequestService.RemoveTranslationRequest(cancelRequest);
        if (translationRequest != null)
        {
            return Ok(translationRequest);
        }

        return NotFound(translationRequest);
    }

    /// <summary>
    /// Retries an existing translation request
    /// Does not delete the current one, just reques
    /// The request with the same information
    /// </summary>
    /// <param name="retryRequest">The translation request to retry</param>
    /// <response code="200">Returns the new translation request</response>
    /// <response code="404">If the translation request was not found</response>
    /// <response code="500">If there was an error checking for updates</response>
    /// <returns>ActionResult containing the new translation request if found, or NotFound if the request doesn't exist</returns>
    [HttpPost("retry")]
    public async Task<ActionResult<string>> RetryTranslationRequest([FromBody] TranslationRequest retryRequest)
    {
        var newTranslationRequest = await _translationRequestService.RetryTranslationRequest(retryRequest);
        if (newTranslationRequest != null)
        {
            return Ok(newTranslationRequest);
        }

        return NotFound(newTranslationRequest);
    }

    /// <summary>
    /// Re-enqueues queued translation requests so they are placed into the correct Hangfire queue
	    /// based on current priority flags.
	    /// </summary>
	    /// <param name="includeInProgress">If true, also attempts to re-enqueue non-processing in-progress requests.</param>
	    /// <param name="dedupe">If true, removes duplicate queued requests before re-enqueueing.</param>
	    /// <response code="200">Returns counts of re-enqueued and skipped requests</response>
	    /// <response code="500">If there was an error while re-enqueueing requests</response>
	    [HttpPost("reenqueue")]
	    public async Task<ActionResult<ReenqueueQueuedRequestsResponse>> ReenqueueQueuedRequests(
	        [FromQuery] bool includeInProgress = false,
	        [FromQuery] bool dedupe = true)
	    {
	        var removedDuplicates = 0;
	        var skippedDuplicateProcessing = 0;
	        if (dedupe)
	        {
	            (removedDuplicates, skippedDuplicateProcessing) =
	                await _translationRequestService.DedupeQueuedRequests(includeInProgress);
	        }

	        var (reenqueued, skippedProcessing) =
	            await _translationRequestService.ReenqueueQueuedRequests(includeInProgress);

	        var message = $"Re-enqueued {reenqueued} request(s). " +
	                      $"Removed {removedDuplicates} duplicate(s). " +
	                      $"Skipped {skippedProcessing} processing job(s). " +
	                      $"Skipped {skippedDuplicateProcessing} processing duplicate(s).";

	        return Ok(new ReenqueueQueuedRequestsResponse
	        {
	            RemovedDuplicates = removedDuplicates,
	            SkippedDuplicateProcessing = skippedDuplicateProcessing,
	            Reenqueued = reenqueued,
	            SkippedProcessing = skippedProcessing,
	            Message = message
	        });
	    }
}
