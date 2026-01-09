using DeepL;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Configuration;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class TranslationRequestService : ITranslationRequestService
{

    private static bool IsActiveStatus(TranslationStatus status) =>
        status == TranslationStatus.Pending || status == TranslationStatus.InProgress;
    
    private readonly LingarrDbContext _dbContext;
    private readonly ITranslationWorkerService _workerService;
    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IProgressService _progressService;
    private readonly IStatisticsService _statisticsService;
    private readonly Lazy<IMediaService> _mediaServiceLazy;
    private readonly ISettingService _settingService;
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly ILogger<TranslationRequestService> _logger;
    private readonly ITranslationCancellationService _cancellationService;
    private readonly IMediaStateService _mediaStateService;
    static private Dictionary<int, CancellationTokenSource> _asyncTranslationJobs = new Dictionary<int, CancellationTokenSource>();

    public TranslationRequestService(
        LingarrDbContext dbContext,
        ITranslationWorkerService workerService,
        IHubContext<TranslationRequestsHub> hubContext,
        ITranslationServiceFactory translationServiceFactory,
        IProgressService progressService,
        IStatisticsService statisticsService,
        Lazy<IMediaService> mediaServiceLazy,
        ISettingService settingService,
        IBatchFallbackService batchFallbackService,
        ILogger<TranslationRequestService> logger,
        ITranslationCancellationService cancellationService,
        IMediaStateService mediaStateService)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _workerService = workerService;
        _translationServiceFactory = translationServiceFactory;
        _progressService = progressService;
        _statisticsService = statisticsService;
        _mediaServiceLazy = mediaServiceLazy;
        _settingService = settingService;
        _batchFallbackService = batchFallbackService;
        _logger = logger;
        _cancellationService = cancellationService;
        _mediaStateService = mediaStateService;
    }

    /// <inheritdoc />
    public async Task<int> CreateRequest(TranslateAbleSubtitle translateAbleSubtitle, bool forcePriority = false)
    {
        var mediaTitle = await FormatMediaTitle(translateAbleSubtitle);
        var translationRequest = new TranslationRequest
        {
            MediaId = translateAbleSubtitle.MediaId,
            Title = mediaTitle,
            SourceLanguage = translateAbleSubtitle.SourceLanguage,
            TargetLanguage = translateAbleSubtitle.TargetLanguage,
            SubtitleToTranslate = translateAbleSubtitle.SubtitlePath,
            MediaType = translateAbleSubtitle.MediaType,
            Status = TranslationStatus.Pending,
            IsActive = true
        };

        return await CreateRequest(translationRequest, forcePriority);
    }

    /// <inheritdoc />
    public async Task<int> CreateRequest(TranslationRequest translationRequest)
    {
        return await CreateRequest(translationRequest, false);
    }

    public async Task<int> CreateRequest(TranslationRequest translationRequest, bool forcePriority)
    {
        if (!forcePriority)
        {
            var existingId = await _dbContext.TranslationRequests
                .Where(tr =>
                    tr.MediaId == translationRequest.MediaId &&
                    tr.MediaType == translationRequest.MediaType &&
                    tr.SourceLanguage == translationRequest.SourceLanguage &&
                    tr.TargetLanguage == translationRequest.TargetLanguage &&
                    (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress))
                .Select(tr => tr.Id)
                .FirstOrDefaultAsync();

            if (existingId != 0)
            {
                _logger.LogInformation(
                    "Skipping duplicate translation request for media {MediaId} {Source}->{Target} (subtitle={SubtitlePath}). Existing request {RequestId} is still active.",
                    translationRequest.MediaId,
                    translationRequest.SourceLanguage,
                    translationRequest.TargetLanguage,
                    translationRequest.SubtitleToTranslate ?? "<embedded>",
                    existingId);
                return existingId;
            }
        }

        // Create a new TranslationRequest to not keep ID and JobID
        // Look up media priority to initialize IsPriority on the request
        var isPriority = forcePriority || await GetMediaPriorityAsync(translationRequest.MediaId, translationRequest.MediaType);
        
        var translationRequestCopy = new TranslationRequest
        {
            MediaId = translationRequest.MediaId,
            Title = translationRequest.Title,
            SourceLanguage = translationRequest.SourceLanguage,
            TargetLanguage = translationRequest.TargetLanguage,
            SubtitleToTranslate = translationRequest.SubtitleToTranslate,
            MediaType = translationRequest.MediaType,
            Status = TranslationStatus.Pending,
            IsActive = true,
            IsPriority = isPriority
        };

        _dbContext.TranslationRequests.Add(translationRequestCopy);
        
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            // Race condition: another process created the same request between our check and insert.
            // This is expected behavior - the dedupe constraint did its job. Return the existing request ID.
            _logger.LogDebug(
                "Race condition avoided: translation request for {Title} ({MediaType} {MediaId}) {Source}->{Target} already created by another process.",
                translationRequest.Title,
                translationRequest.MediaType,
                translationRequest.MediaId,
                translationRequest.SourceLanguage,
                translationRequest.TargetLanguage);
            
            // Clear the failed entity from the change tracker
            _dbContext.ChangeTracker.Clear();
            
            // Find and return the existing request
            var existingRequest = await _dbContext.TranslationRequests
                .Where(tr =>
                    tr.MediaId == translationRequest.MediaId &&
                    tr.MediaType == translationRequest.MediaType &&
                    tr.SourceLanguage == translationRequest.SourceLanguage &&
                    tr.TargetLanguage == translationRequest.TargetLanguage &&
                    tr.IsActive == true)
                .Select(tr => tr.Id)
                .FirstOrDefaultAsync();
            
            return existingRequest;
        }

        await EnqueueTranslationJobAsync(translationRequestCopy, forcePriority);

        var count = await GetActiveCount();
        await _hubContext.Clients.Group("TranslationRequests").SendAsync("RequestActive", new
        {
            count
        });

        return translationRequestCopy.Id;
    }
    
    /// <inheritdoc />
    public async Task<int> GetActiveCount()
    {
        return await _dbContext.TranslationRequests.CountAsync(translation =>
            translation.Status != TranslationStatus.Cancelled &&
            translation.Status != TranslationStatus.Failed &&
            translation.Status != TranslationStatus.Completed &&
            translation.Status != TranslationStatus.Interrupted);

    }

    /// <inheritdoc />
    public async Task<List<TranslationRequestLog>> GetLogsAsync(int translationRequestId)
    {
        return await _dbContext.TranslationRequestLogs
            .Where(log => log.TranslationRequestId == translationRequestId)
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<TranslationRequest>> GetFailedRequests()
    {
        var requests = await _dbContext.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.Failed)
            .OrderByDescending(tr => tr.CompletedAt)
            .ToListAsync();

        await PopulatePriorityFlagsAsync(requests);
        return requests;
    }

    /// <inheritdoc />
    public async Task<List<TranslationRequest>> GetInProgressRequests()
    {
        var requests = await _dbContext.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.InProgress)
            .OrderByDescending(tr => tr.CreatedAt)
            .ToListAsync();

        await PopulatePriorityFlagsAsync(requests);
        return requests;
    }

    /// <inheritdoc />
    public async Task<int> UpdateActiveCount()
    {
        var count = await GetActiveCount();
        await _hubContext.Clients.Group("TranslationRequests").SendAsync("RequestActive", new
        {
            count
        });
        
        return count;
    }
    
    /// <inheritdoc />
    public async Task<string?> CancelTranslationRequest(TranslationRequest cancelRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == cancelRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }

        // Trigger cooperative cancellation for running jobs
        // This will signal the job to stop at its next cancellation check point
        _cancellationService.CancelJob(translationRequest.Id);

        // Also cancel any async translation jobs
        if (_asyncTranslationJobs.ContainsKey(translationRequest.Id))
        {
            await _asyncTranslationJobs[translationRequest.Id].CancelAsync();
        }

        if (translationRequest.Status != TranslationStatus.Completed &&
            translationRequest.Status != TranslationStatus.Failed &&
            translationRequest.Status != TranslationStatus.Cancelled &&
            translationRequest.Status != TranslationStatus.Interrupted)
        {
            translationRequest.CompletedAt = DateTime.UtcNow;
            translationRequest.Status = TranslationStatus.Cancelled;
            translationRequest.IsActive = null;
            await _dbContext.SaveChangesAsync();
            await ClearMediaHash(translationRequest);
            await UpdateActiveCount();
            await UpdateMediaState(translationRequest);
            await _progressService.Emit(translationRequest, 0);
        }

        return $"Translation request with id {cancelRequest.Id} has been cancelled";
    }
    
    /// <inheritdoc />
    public async Task<string?> RemoveTranslationRequest(TranslationRequest cancelRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == cancelRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }
        
        _dbContext.TranslationRequests.Remove(translationRequest);
        await _dbContext.SaveChangesAsync();
        await UpdateActiveCount();
        await UpdateMediaState(translationRequest);
        
        return $"Translation request with id {cancelRequest.Id} has been removed";
    }

    /// <inheritdoc />
    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<int> RetryAllFailedRequests()
    {
        var batchSize = 50;
        var totalRetried = 0;
        var newRequestsCount = 0;

        _logger.LogInformation("Starting batch retry of failed requests...");

        // Loop until we have processed all failed requests
        while (true)
        {
            // 1. Fetch a small batch of failed requests
            // We order by CreatedAt to ensure deterministic processing
            var batch = await _dbContext.TranslationRequests
                .Where(tr => tr.Status == TranslationStatus.Failed)
                .OrderBy(tr => tr.CreatedAt)
                .Take(batchSize)
                .ToListAsync();

            if (!batch.Any())
            {
                break;
            }

            var batchNewRequests = new List<TranslationRequest>();
            var idsToDelete = new List<int>();

            // 2. Identify potentially conflicting active requests
            var mediaIds = batch
                .Where(x => x.MediaId.HasValue)
                .Select(x => x.MediaId!.Value)
                .Distinct()
                .ToList();

            var activeRequestsKeys = new HashSet<(int?, MediaType, string, string)>();
            var moviePriorityMap = new Dictionary<int, bool>();
            var episodePriorityMap = new Dictionary<int, bool>();
            
            if (mediaIds.Any())
            {
                var activeRequests = await _dbContext.TranslationRequests
                    .Where(tr => (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress)
                                 && tr.MediaId != null && mediaIds.Contains(tr.MediaId.Value))
                    .Select(tr => new { tr.MediaId, tr.MediaType, tr.SourceLanguage, tr.TargetLanguage })
                    .ToListAsync();
                
                foreach (var r in activeRequests)
                {
                    activeRequestsKeys.Add((r.MediaId, r.MediaType, r.SourceLanguage, r.TargetLanguage));
                }
                
                // Look up priority status for movies in this batch
                var movieIdsInBatch = batch
                    .Where(x => x.MediaType == MediaType.Movie && x.MediaId.HasValue)
                    .Select(x => x.MediaId!.Value)
                    .Distinct()
                    .ToList();
                if (movieIdsInBatch.Any())
                {
                    moviePriorityMap = await _dbContext.Movies
                        .Where(m => movieIdsInBatch.Contains(m.Id))
                        .Select(m => new { m.Id, m.IsPriority })
                        .ToDictionaryAsync(m => m.Id, m => m.IsPriority);
                }
                
                // Look up priority status for episodes (inherited from Show) in this batch
                var episodeIdsInBatch = batch
                    .Where(x => x.MediaType == MediaType.Episode && x.MediaId.HasValue)
                    .Select(x => x.MediaId!.Value)
                    .Distinct()
                    .ToList();
                if (episodeIdsInBatch.Any())
                {
                    episodePriorityMap = await _dbContext.Episodes
                        .Where(e => episodeIdsInBatch.Contains(e.Id))
                        .Select(e => new { e.Id, Priority = e.Season.Show.IsPriority })
                        .ToDictionaryAsync(e => e.Id, e => e.Priority);
                }
            }

            // 3. Process the batch in a transaction
            // We use an explicit execution strategy because the DbContext is configured with resiliency (retries),
            // which requires manual transactions to be executed within an execution strategy block.
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            
            try 
            {
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _dbContext.Database.BeginTransactionAsync();

                    try
                    {
                        // Clear tracker so we don't have conflicts with the loaded batch entities
                        // when we do bulk deletes or inserts.
                        _dbContext.ChangeTracker.Clear();
                        
                        var groups = batch.GroupBy(tr => new
                        {
                            tr.MediaId,
                            tr.MediaType,
                            tr.SourceLanguage,
                            tr.TargetLanguage
                        });

                        foreach (var group in groups)
                        {
                            var key = (group.Key.MediaId, group.Key.MediaType, group.Key.SourceLanguage, group.Key.TargetLanguage);
                            
                            // If no active request exists for this key, create a new one
                            if (!activeRequestsKeys.Contains(key))
                            {
                                var template = group.OrderByDescending(x => x.CreatedAt).First();
                                
                                // Look up priority from the pre-fetched maps
                                // Retries are treated as priority, but also respect media priority status
                                var isPriority = true; // Default to priority for retries
                                if (template.MediaId.HasValue)
                                {
                                    if (template.MediaType == MediaType.Movie)
                                    {
                                        moviePriorityMap.TryGetValue(template.MediaId.Value, out isPriority);
                                        isPriority = true; // Always priority for retries
                                    }
                                    else if (template.MediaType == MediaType.Episode)
                                    {
                                        episodePriorityMap.TryGetValue(template.MediaId.Value, out isPriority);
                                        isPriority = true; // Always priority for retries
                                    }
                                }
                                
                                var newRequest = new TranslationRequest
                                {
                                    MediaId = template.MediaId,
                                    Title = template.Title,
                                    SourceLanguage = template.SourceLanguage,
                                    TargetLanguage = template.TargetLanguage,
                                    SubtitleToTranslate = template.SubtitleToTranslate,
                                    MediaType = template.MediaType,
                                    Status = TranslationStatus.Pending,
                                    IsActive = true,
                                    IsPriority = isPriority
                                };
                                
                                batchNewRequests.Add(newRequest);
                                
                                // Prevent duplicates within the same batch
                                activeRequestsKeys.Add(key);
                            }
                            
                            // Always delete the failed requests in this group (deduplication/cleanup)
                            idsToDelete.AddRange(group.Select(g => g.Id));
                        }

                        // Execute Deletes
                        if (idsToDelete.Any())
                        {
                            await _dbContext.TranslationRequests
                                .Where(tr => idsToDelete.Contains(tr.Id))
                                .ExecuteDeleteAsync();
                        }

                        // Execute Inserts
                        if (batchNewRequests.Any())
                        {
                            _dbContext.TranslationRequests.AddRange(batchNewRequests);
                            await _dbContext.SaveChangesAsync();
                        }

                        await transaction.CommitAsync();
                        
                        totalRetried += idsToDelete.Count;
                        newRequestsCount += batchNewRequests.Count;
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        // Re-throw to be caught by the outer loop handler or strategy
                        throw; 
                    }
                });
            }
            catch (Exception ex)
            {
                // Transaction rollback already happened inside the execution strategy if needed.
                
                // If it's a unique constraint violation, it means a race condition occurred.
                // We shouldn't crash just because of one batch.
                // However, we need to make sure we don't infinite loop on this batch.
                // Since we couldn't delete the failed requests, they will still be there.
                // To avoid infinite loop, we should simply break and stop processing.
                // The user can try "Retry" again later.
                
                if (ex is DbUpdateException dbEx && IsDuplicateKeyViolation(dbEx)) 
                {
                     _logger.LogWarning("Race condition detected during batch retry (Duplicate Key). Aborting remaining batches.");
                }
                else
                {
                    _logger.LogError(ex, "Unexpected error during batch retry. Aborting remaining batches.");
                }
                
                break;
            }
            
            // 4. Enqueue Jobs for successful batch (must be outside transaction commit)
            foreach (var request in batchNewRequests)
            {
                await EnqueueTranslationJobAsync(request, true);
                await UpdateMediaState(request);
            }
            
            // Small delay to allow other threads/requests to acquire locks if needed
            await Task.Delay(10);
        }

        _logger.LogInformation(
            "Successfully retried {TotalRetried} failed requests, creating {NewCount} new active requests", 
            totalRetried, 
            newRequestsCount);

        var count = await GetActiveCount();
        await _hubContext.Clients.Group("TranslationRequests").SendAsync("RequestActive", new
        {
            count
        });

        return totalRetried;
    }

    /// <inheritdoc />
    public async Task<string?> RetryTranslationRequest(TranslationRequest retryRequest)
    {
        var translationRequest = await _dbContext.TranslationRequests.FirstOrDefaultAsync(
            translationRequest => translationRequest.Id == retryRequest.Id);
        if (translationRequest == null)
        {
            return null;
        }

        // Retries are treated as priority so they jump ahead of existing backlog
        int newTranslationRequestId = await CreateRequest(translationRequest, true);
        
        // Remove the old failed request to clean up
        _dbContext.TranslationRequests.Remove(translationRequest);
        await _dbContext.SaveChangesAsync();
        await UpdateActiveCount();
        await UpdateMediaState(translationRequest);

        return $"Translation request with id {retryRequest.Id} has been restarted, new job id {newTranslationRequestId}";
    }
    
    /// <inheritdoc />
    public async Task<TranslationRequest> UpdateTranslationRequest(TranslationRequest translationRequest,
        TranslationStatus status, string? jobId = null)
    {
        var request = await _dbContext.TranslationRequests.FindAsync(translationRequest.Id);
        if (request == null)
        {
            throw new NotFoundException($"TranslationRequest with ID {translationRequest.Id} not found.");
        }

        if (jobId != null)
        {
            request.JobId = jobId;
        }

        // Check if the request is already in a terminal state
        // This prevents "ghost" jobs from previous runs or duplicates from
        // overwriting a Cancelled/Completed status back to InProgress
        if (status == TranslationStatus.InProgress && 
            (request.Status == TranslationStatus.Cancelled || 
             request.Status == TranslationStatus.Completed ||
             request.Status == TranslationStatus.Failed))
        {
            // Throwing TaskCanceledException will cause the job to abort gracefully (mostly)
            // or at least stop processing
            throw new TaskCanceledException($"Request {request.Id} is already in state {request.Status}, aborting update to {status}");
        }

        request.Status = status;
        request.IsActive = IsActiveStatus(status) ? true : null;
        await _dbContext.SaveChangesAsync();
        await UpdateActiveCount();

        return request;
    }
    
    /// <inheritdoc />
    public async Task ResumeTranslationRequests()
    {
        // With the new worker service approach, we just need to:
        // 1. Reset InProgress jobs to Pending (they were interrupted)
        // 2. Signal the worker service to pick up work
        
        var resetCount = await _dbContext.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(tr => tr.Status, TranslationStatus.Pending));
        
        if (resetCount > 0)
        {
            _logger.LogInformation(
                "Reset {Count} interrupted translation request(s) to Pending status",
                resetCount);
        }
        
        // Signal worker service that work may be available
        _workerService.Signal();
    }

    /// <inheritdoc />
    public async Task<(int Reenqueued, int SkippedProcessing)> ReenqueueQueuedRequests(bool includeInProgress = false)
    {
        var statuses = includeInProgress
            ? new[] { TranslationStatus.Pending, TranslationStatus.InProgress }
            : new[] { TranslationStatus.Pending };

        var requests = await _dbContext.TranslationRequests
            .Where(tr => statuses.Contains(tr.Status))
            .ToListAsync();

        var reenqueued = 0;
        var skippedProcessing = 0;

        foreach (var request in requests)
        {
            // Skip InProgress jobs - they're being actively processed by worker
            if (request.Status == TranslationStatus.InProgress)
            {
                skippedProcessing++;
                continue;
            }

            // For Pending jobs, trigger cooperative cancellation and reset
            _cancellationService.CancelJob(request.Id);
            
            // Mark as Pending to be picked up by worker
            request.Status = TranslationStatus.Pending;
            reenqueued++;
        }

        await _dbContext.SaveChangesAsync();
        
        // Signal worker service that work is available
        _workerService.Signal();

        _logger.LogInformation(
            "Re-enqueued {ReenqueuedCount} translation request(s). Skipped {SkippedProcessingCount} currently processing job(s).",
            reenqueued,
            skippedProcessing);

        return (reenqueued, skippedProcessing);
    }

    /// <inheritdoc />
    public async Task<(int RemovedDuplicates, int SkippedProcessing)> DedupeQueuedRequests(bool includeInProgress = false)
    {
        var statuses = includeInProgress
            ? new[] { TranslationStatus.Pending, TranslationStatus.InProgress }
            : new[] { TranslationStatus.Pending };

        var requests = await _dbContext.TranslationRequests
            .Where(tr => statuses.Contains(tr.Status))
            .OrderBy(tr => tr.CreatedAt)
            .ThenBy(tr => tr.Id)
            .ToListAsync();

        var duplicatesToRemove = new List<TranslationRequest>();
        var skippedProcessing = 0;

        foreach (var group in requests.GroupBy(tr => new
                 {
                     tr.MediaId,
                     tr.MediaType,
                     tr.SourceLanguage,
                     tr.TargetLanguage
                 }))
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            var orderedGroup = group
                .OrderBy(tr => tr.CreatedAt)
                .ThenBy(tr => tr.Id)
                .ToList();

            TranslationRequest? canonical = null;
            
            // Find the canonical request - prefer InProgress, then oldest
            foreach (var candidate in orderedGroup)
            {
                if (candidate.Status == TranslationStatus.InProgress)
                {
                    canonical = candidate;
                    break;
                }
            }

            canonical ??= orderedGroup.First();

            foreach (var duplicate in orderedGroup)
            {
                if (duplicate.Id == canonical.Id)
                {
                    continue;
                }

                // Skip InProgress duplicates - they're being actively processed
                if (duplicate.Status == TranslationStatus.InProgress)
                {
                    skippedProcessing++;
                    continue;
                }

                // Cancel the job if it's running (cooperative cancellation)
                _cancellationService.CancelJob(duplicate.Id);

                duplicatesToRemove.Add(duplicate);
            }
        }

        if (duplicatesToRemove.Count > 0)
        {
            _dbContext.TranslationRequests.RemoveRange(duplicatesToRemove);
            await _dbContext.SaveChangesAsync();
            await UpdateActiveCount();
        }

        var removedDuplicates = duplicatesToRemove.Count;

        _logger.LogInformation(
            "Removed {RemovedCount} duplicate translation request(s). Skipped {SkippedCount} processing duplicate(s).",
            removedDuplicates,
            skippedProcessing);

        return (removedDuplicates, skippedProcessing);
    }

    /// <inheritdoc />
    public async Task<(int Cancelled, int SkippedProcessing)> CancelAllQueuedRequests(bool includeInProgress = false)
    {
        var statuses = includeInProgress
            ? new[] { TranslationStatus.Pending, TranslationStatus.InProgress }
            : new[] { TranslationStatus.Pending };

        var requests = await _dbContext.TranslationRequests
            .Where(tr => statuses.Contains(tr.Status))
            .ToListAsync();

        var cancelled = 0;
        var skippedProcessing = 0;

        foreach (var request in requests)
        {
            // Check if job is currently processing (InProgress status)
            var isProcessing = request.Status == TranslationStatus.InProgress;
            
            // Trigger cooperative cancellation for running jobs
            _cancellationService.CancelJob(request.Id);

            if (isProcessing)
            {
                skippedProcessing++;
            }
            cancelled++;
        }

        // Bulk update Database Status
        // This is much faster than saving each entity individually
        if (requests.Count > 0)
        {
            var requestIds = requests.Select(r => r.Id).ToList();
            var now = DateTime.UtcNow;
            
            await _dbContext.TranslationRequests
                .Where(r => requestIds.Contains(r.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TranslationStatus.Cancelled)
                    .SetProperty(r => r.IsActive, (bool?)null) // Use explicit cast for ExecuteUpdate
                    .SetProperty(r => r.CompletedAt, now));
            
            // Bulk clear media hashes
            var movieIds = requests
                .Where(r => r.MediaType == MediaType.Movie && r.MediaId.HasValue)
                .Select(r => (int)r.MediaId!)
                .Distinct()
                .ToList();
                
            if (movieIds.Count > 0)
            {
                await _dbContext.Movies
                    .Where(m => movieIds.Contains(m.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.MediaHash, string.Empty));
            }
            
            var episodeIds = requests
                .Where(r => r.MediaType == MediaType.Episode && r.MediaId.HasValue)
                .Select(r => (int)r.MediaId!)
                .Distinct()
                .ToList();
                
            if (episodeIds.Count > 0)
            {
                await _dbContext.Episodes
                    .Where(e => episodeIds.Contains(e.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.MediaHash, string.Empty));
            }
            
            await UpdateActiveCount();

            // Update in-memory objects to reflect the new state
            foreach (var req in requests)
            {
                req.Status = TranslationStatus.Cancelled;
                req.CompletedAt = now;
            }

            // Emit throttled progress signals
            await _progressService.EmitBatch(requests, 0);
        }

        _logger.LogInformation(
            "Cancelled {CancelledCount} translation request(s). {ProcessingCount} were actively processing (cancellation signal sent).",
            cancelled,
            skippedProcessing);

        return (cancelled, skippedProcessing);
    }

    /// <inheritdoc />
    public async Task<int> RefreshPriorityForMedia(MediaType mediaType, int mediaId)
    {
        // Update the persisted IsPriority column on pending translation requests for this media.
        // This ensures priority ordering is applied correctly when the worker picks up jobs.
        bool isPriority = false;
        
        // Look up the media's current priority status
        if (mediaType == MediaType.Movie)
        {
            isPriority = await _dbContext.Movies
                .Where(m => m.Id == mediaId)
                .Select(m => m.IsPriority)
                .FirstOrDefaultAsync();
        }
        else if (mediaType == MediaType.Episode)
        {
            isPriority = await _dbContext.Episodes
                .Where(e => e.Id == mediaId)
                .Select(e => e.Season.Show.IsPriority)
                .FirstOrDefaultAsync();
        }
        
        // Update all pending requests for this media with the new priority
        var updated = await _dbContext.TranslationRequests
            .Where(tr => tr.MediaId == mediaId && 
                         tr.MediaType == mediaType && 
                         tr.Status == TranslationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(tr => tr.IsPriority, isPriority));
        
        _logger.LogInformation(
            "Priority changed for {MediaType} {MediaId} - updated {Count} pending request(s) to IsPriority={IsPriority}",
            mediaType, mediaId, updated, isPriority);
        
        // Signal the worker service that priority has changed (optional optimization)
        _workerService.Signal();
        
        return updated;
    }


    

    private async Task<List<TranslationRequest>> OrderRequestsForPriorityProcessingAsync(List<TranslationRequest> requests)
    {
        if (requests.Count == 0)
        {
            return requests;
        }

        // Priority is now persisted on the TranslationRequest, no need to populate
        return requests
            .OrderByDescending(r => r.IsPriority)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PagedResult<TranslationRequest>> GetTranslationRequests(
        string? searchQuery,
        string? orderBy,
        bool ascending,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.TranslationRequests
            .AsSplitQuery()
            .Where(tr => tr.Status == TranslationStatus.Pending)
            .AsQueryable();

        if (!string.IsNullOrEmpty(searchQuery))
        {
            query = query.Where(translationRequest => translationRequest.Title.ToLower().Contains(searchQuery.ToLower()));
        }
    
        query = orderBy switch
        {
            "Title" => ascending 
                ? query.OrderBy(m => m.Title) 
                : query.OrderByDescending(m => m.Title),
            "CreatedAt" => ascending
                ? query.OrderByDescending(tr => tr.CreatedAt)
                : query.OrderBy(tr => tr.CreatedAt),
            "CompletedAt" => ascending
                ? query.OrderByDescending(tr => tr.CompletedAt)
                : query.OrderBy(tr => tr.CompletedAt),
            _ => ascending
                ? query.OrderByDescending(tr => tr.CreatedAt)
                : query.OrderBy(tr => tr.CreatedAt)
        };
        
        var totalCount = await query.CountAsync();
        var requests = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        await PopulatePriorityFlagsAsync(requests);

        return new PagedResult<TranslationRequest>
        {
            Items = requests,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
    
    /// <inheritdoc />
    public async Task ClearMediaHash(TranslationRequest translationRequest)
    {
        if (!translationRequest.MediaId.HasValue) 
        {
            return;
        }

        try
        {
            switch (translationRequest.MediaType)
            {
                case MediaType.Movie:
                    var movie = await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == translationRequest.MediaId.Value);
                    if (movie != null)
                    {
                        movie.MediaHash = string.Empty;
                    }
                    break;
                
                case MediaType.Episode:
                    var episode = await _dbContext.Episodes.FirstOrDefaultAsync(e => e.Id == translationRequest.MediaId.Value);
                    if (episode != null)
                    {
                        episode.MediaHash = string.Empty;
                    }
                    break;
            }
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ignore concurrency exceptions here - if another process updated the media,
            // the hash is likely already cleared or we can't safely clear it anyway without reloading.
            // Since this is just a cache invalidation optimization, it's safe to skip if we hit a race.
            _logger.LogDebug("Concurrency exception while clearing media hash for {MediaType} {MediaId} - skipping", 
                translationRequest.MediaType, translationRequest.MediaId);
        }
    }

    private async Task PopulatePriorityFlagsAsync(List<TranslationRequest> requests)
    {
        if (!requests.Any())
        {
            return;
        }

        var movieIds = requests
            .Where(r => r.MediaType == MediaType.Movie && r.MediaId.HasValue)
            .Select(r => r.MediaId!.Value)
            .Distinct()
            .ToList();

        var episodeIds = requests
            .Where(r => r.MediaType == MediaType.Episode && r.MediaId.HasValue)
            .Select(r => r.MediaId!.Value)
            .Distinct()
            .ToList();

        // Optimization: Use projection to fetch only the IsPriority flag.
        // This avoids fetching the entire Movie entity and tracking it.
        var moviePriorityMap = movieIds.Count == 0
            ? new Dictionary<int, bool>()
            : await _dbContext.Movies
                .Where(m => movieIds.Contains(m.Id))
                .Select(m => new { m.Id, m.IsPriority })
                .ToDictionaryAsync(m => m.Id, m => m.IsPriority);

        // Optimization: Use projection to fetch only the Show's IsPriority flag.
        // This avoids joining and fetching full Episode, Season, and Show entities.
        var episodePriorityMap = episodeIds.Count == 0
            ? new Dictionary<int, bool>()
            : await _dbContext.Episodes
                .Where(e => episodeIds.Contains(e.Id))
                .Select(e => new { e.Id, Priority = e.Season.Show.IsPriority })
                .ToDictionaryAsync(e => e.Id, e => e.Priority);

        foreach (var request in requests)
        {
            request.IsPriority = false;
            
            if (!request.MediaId.HasValue)
            {
                continue;
            }

            switch (request.MediaType)
            {
                case MediaType.Movie:
                    if (moviePriorityMap.TryGetValue(request.MediaId.Value, out var moviePriority) && moviePriority)
                    {
                        request.IsPriority = true;
                    }
                    break;

                case MediaType.Episode:
                    if (episodePriorityMap.TryGetValue(request.MediaId.Value, out var episodePriority) && episodePriority)
                    {
                        request.IsPriority = true;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Looks up the priority status of the media entity (Movie or Show).
    /// </summary>
    /// <param name="mediaId">The ID of the media entity</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    /// <returns>True if the media is marked as priority, false otherwise</returns>
    private async Task<bool> GetMediaPriorityAsync(int? mediaId, MediaType mediaType)
    {
        if (!mediaId.HasValue) return false;
        
        if (mediaType == MediaType.Movie)
        {
            return await _dbContext.Movies
                .Where(m => m.Id == mediaId.Value)
                .Select(m => m.IsPriority)
                .FirstOrDefaultAsync();
        }
        else if (mediaType == MediaType.Episode)
        {
            return await _dbContext.Episodes
                .Where(e => e.Id == mediaId.Value)
                .Select(e => e.Season.Show.IsPriority)
                .FirstOrDefaultAsync();
        }
        return false;
    }

    private async Task EnqueueTranslationJobAsync(TranslationRequest translationRequest, bool forcePriority)
    {
        // Simply set status to Pending - TranslationWorkerService will pick it up
        // Priority ordering is handled by the worker service using the IsPriority column
        translationRequest.Status = TranslationStatus.Pending;
        translationRequest.JobId = null; // No longer using Hangfire job IDs
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation(
            "Translation request {RequestId} enqueued for processing (Priority: {IsPriority})",
            translationRequest.Id,
            translationRequest.IsPriority);
        
        // Signal the worker service that new work is available
        _workerService.Signal();
    }

    /// <inheritdoc />
    public async Task<BatchTranslatedLine[]> TranslateContentAsync(
        TranslateAbleSubtitleContent translateAbleContent,
        CancellationToken parentCancellationToken)
    {
        // Prepare TranslationRequest Object
        var translationRequest = new TranslationRequest
        {
            MediaId = await GetMediaId(translateAbleContent.ArrMediaId, translateAbleContent.MediaType),
            Title = translateAbleContent.Title,
            SourceLanguage = translateAbleContent.SourceLanguage,
            TargetLanguage = translateAbleContent.TargetLanguage,
            MediaType = translateAbleContent.MediaType,
            Status = TranslationStatus.InProgress,
            IsActive = true
        };

        // Link cancel token with new source to be able to cancel the async translation
        var asyncTranslationCancellationTokenSource = new CancellationTokenSource();
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken, asyncTranslationCancellationTokenSource.Token);
        var cancellationToken = cancellationTokenSource.Token;

        try
        {
            BatchTranslatedLine[]? results;
            // Get Translation Settings
            var settings = await _settingService.GetSettings([
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.EnableBatchFallback,
                SettingKeys.Translation.MaxBatchSplitAttempts
            ]);
            var serviceType = settings[SettingKeys.Translation.ServiceType];
            var translationService = _translationServiceFactory.CreateTranslationService(
                serviceType
            );

            // Add TranslationRequest
            _dbContext.TranslationRequests.Add(translationRequest);
            await _dbContext.SaveChangesAsync();
            await UpdateActiveCount();

            // Add translation as a async translation request with cancellation source
            _asyncTranslationJobs.Add(translationRequest.Id, cancellationTokenSource);


            // Process Translation
            if (settings[SettingKeys.Translation.UseBatchTranslation] == "true"
                && translateAbleContent.Lines.Count > 1
                && translationService is IBatchTranslationService batchService)
            {
                _logger.LogInformation("Processing batch translation request with {lineCount} lines from {sourceLanguage} to {targetLanguage}",
                    translateAbleContent.Lines.Count, translateAbleContent.SourceLanguage, translateAbleContent.TargetLanguage);

                var subtitleTranslator = new SubtitleTranslationService(translationService, _logger, null, _batchFallbackService);
                var totalSize = translateAbleContent.Lines.Count;
                var maxBatchSize = settings[SettingKeys.Translation.MaxBatchSize];
                var stripSubtitleFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
                var enableBatchFallback = settings[SettingKeys.Translation.EnableBatchFallback] == "true";
                var maxBatchSplitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var splitAttempts)
                    ? splitAttempts
                    : 3;
                var maxSize = int.TryParse(maxBatchSize,
                    out var batchSize)
                    ? batchSize
                    : 10000;

                _logger.LogDebug("Batch translation configuration: maxSize={maxSize}, stripFormatting={stripFormatting}, totalLines={totalLines}, fallback={fallback}",
                    maxSize, stripSubtitleFormatting, totalSize, enableBatchFallback);

                if (maxSize != 0 && totalSize > maxSize)
                {
                    _logger.LogWarning(
                        "Batch size ({Size}) exceeds configured maximum ({Max}). Processing in smaller batches.",
                        totalSize, maxSize);
                    results = await ChunkLargeBatch(
                        translateAbleContent,
                        translationService,
                        batchService,
                        translationRequest,
                        maxSize,
                        stripSubtitleFormatting,
                        cancellationToken);

                    // Handle completion now since we early exit here
                    await HandleAsyncTranslationCompletion(translationRequest, serviceType, results, cancellationToken);
                    return results; 
                }

                _logger.LogInformation("Processing batch translation within size limits. Converting {lineCount} lines to subtitle items",
                    translateAbleContent.Lines.Count);

                // Convert translateAbleContent items to SubtitleItems for ProcessSubtitleBatch
                var subtitleItems = translateAbleContent.Lines.Select(item => new SubtitleItem
                {
                    Position = item.Position,
                    Lines = new List<string> { item.Line },
                    PlaintextLines = new List<string> { item.Line }
                }).ToList();

                _logger.LogDebug("Starting batch subtitle processing with {itemCount} subtitle items", subtitleItems.Count);

                await subtitleTranslator.ProcessSubtitleBatch(
                    subtitleItems,
                    batchService,
                    translateAbleContent.SourceLanguage,
                    translateAbleContent.TargetLanguage,
                    stripSubtitleFormatting,
                    enableFallback: enableBatchFallback,
                    maxSplitAttempts: maxBatchSplitAttempts,
                    fileIdentifier: translateAbleContent.Title ?? "API",
                    cancellationToken: cancellationToken);

                results = subtitleItems.Select(subtitle => new BatchTranslatedLine
                {
                    Position = subtitle.Position,
                    Line = string.Join(" ", subtitle.TranslatedLines)
                }).ToArray();

                _logger.LogInformation("Batch translation completed successfully. Processed {resultCount} translated lines", results.Length);
            }
            else
            {
                _logger.LogInformation("Using individual line translation for {lineCount} lines from {sourceLanguage} to {targetLanguage}",
                    translateAbleContent.Lines.Count,
                    translateAbleContent.SourceLanguage,
                    translateAbleContent.TargetLanguage);

                var subtitleTranslator = new SubtitleTranslationService(translationService, _logger);
                var tempResults = new List<BatchTranslatedLine>();

                int iteration = 1;
                int total = translateAbleContent.Lines.Count();
                foreach (var item in translateAbleContent.Lines)
                {
                    var translateLine = new TranslateAbleSubtitleLine
                    {
                        SubtitleLine = item.Line,
                        SourceLanguage = translateAbleContent.SourceLanguage,
                        TargetLanguage = translateAbleContent.TargetLanguage
                    };

                    var translatedText = await subtitleTranslator.TranslateSubtitleLine(
                        translateLine,
                        cancellationToken);

                    tempResults.Add(new BatchTranslatedLine
                    {
                        Position = item.Position,
                        Line = translatedText
                    });

                    int progress = (int)Math.Round((double)iteration * 100 / total);
                    await _progressService.Emit(translationRequest, progress);
                    iteration++;
                }

                _logger.LogInformation("Individual line translation completed. Processed {resultCount} lines", tempResults.Count);
                results = tempResults.ToArray();
            }

            await HandleAsyncTranslationCompletion(translationRequest, serviceType, results, cancellationToken);
            return results;
        }
        catch (TaskCanceledException)
        {
            translationRequest.CompletedAt = DateTime.UtcNow;
            translationRequest.Status = TranslationStatus.Cancelled;
            translationRequest.IsActive = null;
            await _dbContext.SaveChangesAsync();
            await UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating subtitle content");
            translationRequest.CompletedAt = DateTime.UtcNow;
            translationRequest.Status = TranslationStatus.Failed;
            translationRequest.IsActive = null;
            await _dbContext.SaveChangesAsync();
            await UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
        }
        finally
        {
            // Remove async translation from async translation jobs
            _asyncTranslationJobs.Remove(translationRequest.Id);
        }
    }

    /// <summary>
    /// Get the Lingarr's media id for the Episode or the Show
    /// </summary>
    private async Task<int> GetMediaId(int arrMediaId, MediaType mediaType)
    {
        switch (mediaType)
        {
            case MediaType.Episode:
                return await _mediaServiceLazy.Value.GetEpisodeIdOrSyncFromSonarrEpisodeId(arrMediaId);
            case MediaType.Movie:
                return await _mediaServiceLazy.Value.GetMovieIdOrSyncFromRadarrMovieId(arrMediaId);
            default:
                _logger.LogWarning("Unsupported media type: {MediaType} for translate content async", mediaType);
                return 0;
        }
    }

    /// <summary>
    /// Handles a successful async translation job
    /// </summary>
    private async Task HandleAsyncTranslationCompletion(
        TranslationRequest translationRequest,
        string serviceType,
        BatchTranslatedLine[] results,
        CancellationToken cancellationToken)
    {
        await _statisticsService.UpdateTranslationStatisticsFromLines(translationRequest, serviceType, results);

        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Completed;
        translationRequest.IsActive = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100); // Tells the frontend to update translation request to a finished state
    }

    /// <summary>
    /// Processes a large batch by breaking it into smaller batches
    /// </summary>
    private async Task<BatchTranslatedLine[]> ChunkLargeBatch(
        TranslateAbleSubtitleContent translateAbleSubtitleContent,
        ITranslationService translationService,
        IBatchTranslationService batchService,
        TranslationRequest translationRequest,
        int maxBatchSize,
        bool stripSubtitleFormatting,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchTranslatedLine>();
        var currentBatch = new List<SubtitleItem>();
        var subtitleTranslator = new SubtitleTranslationService(translationService, _logger);

        var totalLines = translateAbleSubtitleContent.Lines.Count;
        var totalBatches = (int)Math.Ceiling((double)totalLines / maxBatchSize);
        var processedBatches = 1;

        foreach (var item in translateAbleSubtitleContent.Lines)
        {
            if (currentBatch.Count >= maxBatchSize)
            {
                await ProcessBatch(currentBatch, subtitleTranslator, batchService,
                    translateAbleSubtitleContent.SourceLanguage, translateAbleSubtitleContent.TargetLanguage,
                    stripSubtitleFormatting, results, cancellationToken);
                currentBatch.Clear();

                // Report progress
                // await _progressService.Emit(tra)
                processedBatches++;
                int progress = (int)Math.Round((double)processedBatches * 100 / totalBatches);
                await _progressService.Emit(translationRequest, progress);
            }

            currentBatch.Add(new SubtitleItem
            {
                Position = item.Position,
                Lines =
                [
                    item.Line
                ],
                PlaintextLines =
                [
                    item.Line
                ]
            });
        }

        if (currentBatch.Count > 0)
        {
            await ProcessBatch(currentBatch, subtitleTranslator, batchService,
                translateAbleSubtitleContent.SourceLanguage, translateAbleSubtitleContent.TargetLanguage,
                stripSubtitleFormatting, results, cancellationToken);
        }

        return results.ToArray();
    }

    /// <summary>
    /// Processes a single batch and adds results to the results collection
    /// </summary>
    private async Task ProcessBatch(
        List<SubtitleItem> batch,
        SubtitleTranslationService subtitleTranslator,
        IBatchTranslationService batchService,
        string sourceLanguage,
        string targetLanguage,
        bool stripSubtitleFormatting,
        List<BatchTranslatedLine> results,
        CancellationToken cancellationToken)
    {
        await subtitleTranslator.ProcessSubtitleBatch(
            batch,
            batchService,
            sourceLanguage,
            targetLanguage,
            stripSubtitleFormatting,
            enableFallback: false, // Fallback disabled for chunked batches (already chunking)
            maxSplitAttempts: 3,
            fileIdentifier: "API-chunked",
            cancellationToken: cancellationToken);

        results.AddRange(batch.Select(subtitle => new BatchTranslatedLine
        {
            Position = subtitle.Position,
            Line = string.Join(" ", subtitle.TranslatedLines ?? subtitle.Lines)
        }));
    }
    
    /// <summary>
    /// Formats the media title based on the media type and ID.
    /// </summary>
    /// <param name="translateAbleSubtitle">The subtitle information containing media type and ID</param>
    private async Task<string> FormatMediaTitle(TranslateAbleSubtitle translateAbleSubtitle)
    {
        switch (translateAbleSubtitle.MediaType)
        {
            case MediaType.Movie:
                var movieTitle = await _dbContext.Movies
                    .Where(m => m.Id == translateAbleSubtitle.MediaId)
                    .Select(m => m.Title)
                    .FirstOrDefaultAsync();
                return movieTitle ?? "Unknown Movie";

            case MediaType.Episode:
                var episodeInfo = await _dbContext.Episodes
                    .Where(e => e.Id == translateAbleSubtitle.MediaId)
                    .Select(e => new
                    {
                        EpisodeTitle = e.Title,
                        EpisodeNumber = e.EpisodeNumber,
                        SeasonNumber = e.Season.SeasonNumber,
                        ShowTitle = e.Season.Show.Title
                    })
                    .FirstOrDefaultAsync();

                if (episodeInfo == null)
                    return "Unknown Episode";

                // Format: "Show Title - S01E02 - Episode Title"
                return $"{episodeInfo.ShowTitle} - " +
                       $"S{episodeInfo.SeasonNumber:D2}E{episodeInfo.EpisodeNumber:D2} - " +
                       $"{episodeInfo.EpisodeTitle}";

            default:
            throw new ArgumentException($"Unsupported media type: {translateAbleSubtitle.MediaType}");
        }
    }
    
    /// <summary>
    /// Checks if the given exception is a duplicate key violation.
    /// </summary>
    /// <param name="ex">The exception to check</param>
    /// <returns>True if this is a duplicate key violation, false otherwise</returns>
    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        // PostgreSQL error code 23505 = unique_violation
        // SQLite error code 19 = UNIQUE constraint failed
        if (ex.InnerException is Npgsql.PostgresException pgEx)
        {
            if (pgEx.SqlState == "23505")
            {
                return true;
            }
        }
        
        // Fallback: Check message string for common duplicate key error messages
        // This handles cases where the error code might not be propagated correctly or for other DB providers
        var message = ex.InnerException?.Message ?? ex.Message;
        
        if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) || 
            message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) || 
            message.Contains("23505") ||
            message.Contains("duplicate entry", StringComparison.OrdinalIgnoreCase)) // MySQL standard error
        {
            return true;
        }
        
        return false;
    }

    private async Task UpdateMediaState(TranslationRequest request)
    {
        if (!request.MediaId.HasValue) return;

        try
        {
            if (request.MediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies.FindAsync(request.MediaId.Value);
                if (movie != null)
                {
                    await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
                }
            }
            else if (request.MediaType == MediaType.Episode)
            {
                var episode = await _dbContext.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s.Show)
                    .FirstOrDefaultAsync(e => e.Id == request.MediaId.Value);
                if (episode != null)
                {
                    await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update media state for {MediaType} {MediaId}", request.MediaType, request.MediaId);
        }
    }
}
