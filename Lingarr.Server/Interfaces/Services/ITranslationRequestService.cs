using DeepL;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Interfaces.Services;

public interface ITranslationRequestService
{
    /// <summary>
    /// Creates a new translation request for a subtitle file and enqueues it for processing.
    /// </summary>
    /// <param name="translateAbleSubtitle">Details of the subtitle to be translated, including source and target languages</param>
    /// <param name="forcePriority">If true, forces the request to use the priority queue regardless of media priority status</param>
    /// <returns>The ID of the created translation request</returns>
    Task<int> CreateRequest(TranslateAbleSubtitle translateAbleSubtitle, bool forcePriority = false);

    /// <summary>
    /// Creates a new translation request from a translationRequest, creating a new one with the same exact settings.
    /// </summary>
    /// <param name="translationRequest">Translation request to copie</param>
    /// <returns>The ID of the created translation request</returns>
    Task<int> CreateRequest(TranslationRequest translationRequest);

    /// <summary>
    /// Retrieves the count of active translation requests.
    /// </summary>
    /// <returns>Number of translation requests that are neither Cancelled nor Completed</returns>
    Task<int> GetActiveCount();

    /// <summary>
    /// Updates the active count and notifies connected clients via SignalR.
    /// </summary>
    /// <returns>The current count of active translation requests</returns>
    Task<int> UpdateActiveCount();

    /// <summary>
    /// Interrupts all pending and in-progress translation requests for a specific media item.
    /// Intended for cases where the source media file changed and active requests should not continue.
    /// </summary>
    /// <param name="mediaType">Type of media (Movie or Episode)</param>
    /// <param name="mediaId">The ID of the media item</param>
    /// <returns>The number of interrupted requests</returns>
    Task<int> InterruptActiveRequestsForMedia(MediaType mediaType, int mediaId);

	    /// <summary>
	    /// Resumes all pending and in-progress translation requests by re-enqueueing them in the job queue.
	    /// </summary>
	    /// <returns>A task representing the asynchronous operation.</returns>
	    Task ResumeTranslationRequests();

	    /// <summary>
	    /// Re-enqueues queued translation requests so they are placed into the correct Hangfire queue
	    /// based on current priority flags.
	    /// By default, only Pending requests are re-enqueued; InProgress requests are left untouched.
	    /// Requests whose Hangfire job is currently processing are skipped.
	    /// </summary>
	    /// <param name="includeInProgress">If true, also attempts to re-enqueue non-processing InProgress requests.</param>
	    /// <returns>
	    /// Tuple containing (reenqueuedCount, skippedProcessingCount).
	    /// </returns>
	    Task<(int Reenqueued, int SkippedProcessing)> ReenqueueQueuedRequests(bool includeInProgress = false);

	    /// <summary>
	    /// Removes duplicate queued translation requests.
	    /// Duplicates are requests with the same media id/type and source/target language.
	    /// By default, only Pending requests are deduplicated.
	    /// Requests whose Hangfire job is currently processing are skipped.
	    /// </summary>
	    /// <param name="includeInProgress">If true, also attempts to dedupe non-processing InProgress requests.</param>
	    /// <returns>
	    /// Tuple containing (removedDuplicatesCount, skippedProcessingCount).
	    /// </returns>
	    Task<(int RemovedDuplicates, int SkippedProcessing)> DedupeQueuedRequests(bool includeInProgress = false);

	    /// <summary>
	    /// Cancels all pending and optionally in-progress translation requests.
	    /// Requests whose Hangfire job is currently processing are skipped.
	    /// </summary>
	    /// <param name="includeInProgress">If true, also cancels InProgress requests.</param>
	    /// <returns>
	    /// Tuple containing (cancelledCount, skippedProcessingCount).
	    /// </returns>
	    Task<(int Cancelled, int SkippedProcessing)> CancelAllQueuedRequests(bool includeInProgress = false);

    /// <summary>
    /// Refreshes the queue priority for all pending requests associated with the given media.
    /// Deletes existing Hangfire jobs and re-enqueues them to respect the new priority.
    /// Jobs that are currently processing are skipped.
    /// </summary>
    /// <param name="mediaType">Type of media (Movie or Show)</param>
    /// <param name="mediaId">The ID of the movie or show</param>
    /// <returns>The number of translation requests that were moved to a different queue</returns>
    Task<int> RefreshPriorityForMedia(MediaType mediaType, int mediaId);


    /// <summary>
    /// Retrieves a paginated list of translation requests with optional filtering and sorting.
    /// </summary>
    /// <param name="searchQuery">Optional search term to filter requests by title</param>
    /// <param name="orderBy">Property to sort by: "Title", "CreatedAt", or "CompletedAt"</param>
    /// <param name="ascending">Sort direction</param>
    /// <param name="pageNumber">Page number for pagination (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <returns>
    /// A PagedResult containing the requested translation requests and pagination information
    /// </returns>
    Task<PagedResult<TranslationRequest>> GetTranslationRequests(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize);

    /// <summary>
    /// Retrieves the active count plus bounded pending, failed, and in-progress request sections.
    /// </summary>
    /// <param name="searchQuery">Optional search term to filter pending requests by title</param>
    /// <param name="orderBy">Property to sort pending requests by: "Title", "CreatedAt", or "CompletedAt"</param>
    /// <param name="ascending">Sort direction for pending requests</param>
    /// <param name="pageNumber">Pending request page number</param>
    /// <param name="pageSize">Number of pending requests per page</param>
    /// <param name="sectionLimit">Maximum failed and in-progress rows returned</param>
    /// <returns>Overview response for the translations page</returns>
    Task<TranslationRequestsOverviewResponse> GetOverview(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize,
        int sectionLimit);

    /// <summary>
    /// Removes an existing translation request and its associated background job.
    /// </summary>
    /// <param name="cancelRequest">The translation request to remove</param>
    /// <returns>
    /// A message indicating the result of the remove operation, or null if the request wasn't found
    /// </returns>
    Task<string?> RemoveTranslationRequest(
        TranslationRequest cancelRequest
    );

    /// <summary>
    /// Retries all translation requests with Failed status.
    /// </summary>
    /// <returns>Structured retry result with retried and blocked counters.</returns>
    Task<RetryFailedRequestsResponse> RetryAllFailedRequests();

    /// <summary>
    /// Retries failed translation requests that are currently eligible by retry backoff policy.
    /// </summary>
    /// <returns>Structured retry result with retried and blocked counters.</returns>
    Task<RetryFailedRequestsResponse> RetryEligibleFailedRequests();

    /// <summary>
    /// Removes all translation requests with Failed status
    /// </summary>
    /// <returns>Int representing number of removed requests</returns>
    Task<int> RemoveAllFailedRequests();
    
    /// <summary>
    /// Retries an existing translation request.
    /// </summary>
    /// <param name="retryRequest">The translation request to retry</param>
    /// <returns>Structured retry result, or null if the request wasn't found.</returns>
    Task<RetryTranslationRequestResponse?> RetryTranslationRequest(
        TranslationRequest retryRequest);

    /// <summary>
    /// Cancels an existing translation request and its associated background job.
    /// </summary>
    /// <param name="cancelRequest">The translation request to cancel</param>
    /// <returns>
    /// A message indicating the result of the cancellation operation, or null if the request wasn't found
    /// </returns>
    Task<string?> CancelTranslationRequest(
        TranslationRequest cancelRequest
    );

    /// <summary>
    /// Clears the MediaHash property for the associated media entity (Movie or Episode) 
    /// when a translation job fails or is cancelled.
    /// </summary>
    /// <param name="translationRequest">The translation request containing media information</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task ClearMediaHash(TranslationRequest translationRequest);

    /// <summary>
    /// Updates the status and job ID of an existing translation request.
    /// </summary>
    /// <param name="translationRequest">The translation request to update</param>
    /// <param name="jobId">The ID of the associated Hangfire background job</param>
    /// <param name="status">The new status to set</param>
    /// <returns>The updated translation request</returns>
    /// <exception cref="NotFoundException">Thrown when the specified translation request is not found</exception>
    Task<TranslationRequest> UpdateTranslationRequest(
        TranslationRequest translationRequest,
        TranslationStatus status,
        string? jobId = null);

    /// <summary>
    /// Translate subtitle content without using jobs, Used for other Apps API Intergration (ex. Bazarr).
    /// </summary>
    /// <param name="translateAbleContent">The translation to translate</param>
    /// <param name="parentCancellationToken">Token to cancel the translation operation</param>
    /// <returns>The translated lines</returns>
    Task<BatchTranslatedLine[]> TranslateContentAsync(
        TranslateAbleSubtitleContent translateAbleContent,
        CancellationToken parentCancellationToken);

    /// <summary>
    /// Retrieves all persisted log entries for a specific translation request.
    /// </summary>
    /// <param name="translationRequestId">The ID of the translation request</param>
    /// <returns>List of log entries ordered by creation time</returns>
    Task<List<TranslationRequestLog>> GetLogsAsync(int translationRequestId);

    /// <summary>
    /// Retrieves all translation requests with Failed status.
    /// </summary>
    /// <returns>List of all failed translation requests</returns>
    Task<List<TranslationRequest>> GetFailedRequests();

/// <summary>
    /// Retrieves all translation requests with InProgress status.
    /// </summary>
    /// <returns>List of all in-progress translation requests</returns>
    Task<List<TranslationRequest>> GetInProgressRequests();

    /// <summary>
    /// Retrieves recent completed translation requests using offset-based pagination.
    /// </summary>
    /// <param name="offset">Number of records to skip</param>
    /// <param name="limit">Maximum number of requests to return</param>
    /// <returns>
    /// Tuple containing the page of completed requests and the total completed requests count
    /// </returns>
    Task<(List<TranslationRequest> Requests, int TotalCount)> GetRecentCompletedRequests(
        int offset = 0,
        int limit = 10);
}
