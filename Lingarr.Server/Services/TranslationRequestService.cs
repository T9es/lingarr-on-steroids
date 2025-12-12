using DeepL;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
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
    private const string DefaultTranslationQueue = "translation";
    private const string PriorityTranslationQueue = "translation-priority";
    
    private readonly LingarrDbContext _dbContext;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IProgressService _progressService;
    private readonly IStatisticsService _statisticsService;
    private readonly IMediaService _mediaService;
    private readonly ISettingService _settingService;
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly ILogger<TranslationRequestService> _logger;
    private readonly ITranslationCancellationService _cancellationService;
    static private Dictionary<int, CancellationTokenSource> _asyncTranslationJobs = new Dictionary<int, CancellationTokenSource>();

    public TranslationRequestService(
        LingarrDbContext dbContext,
        IBackgroundJobClient backgroundJobClient,
        IHubContext<TranslationRequestsHub> hubContext,
        ITranslationServiceFactory translationServiceFactory,
        IProgressService progressService,
        IStatisticsService statisticsService,
        IMediaService mediaService,
        ISettingService settingService,
        IBatchFallbackService batchFallbackService,
        ILogger<TranslationRequestService> logger,
        ITranslationCancellationService cancellationService)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
        _backgroundJobClient = backgroundJobClient;
        _translationServiceFactory = translationServiceFactory;
        _progressService = progressService;
        _statisticsService = statisticsService;
        _mediaService = mediaService;
        _settingService = settingService;
        _batchFallbackService = batchFallbackService;
        _logger = logger;
        _cancellationService = cancellationService;
    }

    /// <inheritdoc />
    public async Task<int> CreateRequest(TranslateAbleSubtitle translateAbleSubtitle)
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
            Status = TranslationStatus.Pending
        };

        return await CreateRequest(translationRequest);
    }

    /// <inheritdoc />
    public async Task<int> CreateRequest(TranslationRequest translationRequest)
    {
        return await CreateRequest(translationRequest, false);
    }

    public async Task<int> CreateRequest(TranslationRequest translationRequest, bool forcePriority)
    {
        // Create a new TranslationRequest to not keep ID and JobID
        var translationRequestCopy = new TranslationRequest
        {
            MediaId = translationRequest.MediaId,
            Title = translationRequest.Title,
            SourceLanguage = translationRequest.SourceLanguage,
            TargetLanguage = translationRequest.TargetLanguage,
            SubtitleToTranslate = translationRequest.SubtitleToTranslate,
            MediaType = translationRequest.MediaType,
            Status = TranslationStatus.Pending
        };

        _dbContext.TranslationRequests.Add(translationRequestCopy);
        await _dbContext.SaveChangesAsync();

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

        if (translationRequest.JobId != null)
        {
            _backgroundJobClient.Delete(translationRequest.JobId);
        }
        else if (_asyncTranslationJobs.ContainsKey(translationRequest.Id))
        {
            // Maybe an async translation job
            await _asyncTranslationJobs[translationRequest.Id].CancelAsync();
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
        
        return $"Translation request with id {cancelRequest.Id} has been removed";
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
        request.Status = status;
        await _dbContext.SaveChangesAsync();

        return request;
    }
    
    /// <inheritdoc />
    public async Task ResumeTranslationRequests()
    {
        var requests = await _dbContext.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.Pending || 
                         tr.Status == TranslationStatus.InProgress)
            .ToListAsync();

        foreach (var request in requests)
        {
            if (request.JobId == null)
            {
                // Async translation job. Set as Interrupted and don't run
                // Those cannot be resumed
                await UpdateTranslationRequest(request, TranslationStatus.Interrupted);
                continue;
            }

            // Use the same queue selection logic as for new requests
            await EnqueueTranslationJobAsync(request, false);
        }
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
            if (request.JobId != null)
            {
                try
                {
                    var stateData = JobStorage.Current.GetConnection().GetStateData(request.JobId);
                    if (stateData?.Name == ProcessingState.StateName)
                    {
                        skippedProcessing++;
                        continue;
                    }

                    _backgroundJobClient.Delete(request.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete existing Hangfire job {JobId} for request {RequestId} before re-enqueue.",
                        request.JobId,
                        request.Id);
                }
            }

            await EnqueueTranslationJobAsync(request, false);
            reenqueued++;
        }

        _logger.LogInformation(
            "Re-enqueued {ReenqueuedCount} translation request(s). Skipped {SkippedProcessingCount} currently processing job(s).",
            reenqueued,
            skippedProcessing);

        return (reenqueued, skippedProcessing);
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

        var moviePriorityMap = movieIds.Count == 0
            ? new Dictionary<int, bool>()
            : await _dbContext.Movies
                .Where(m => movieIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.IsPriority);

        var episodePriorityMap = episodeIds.Count == 0
            ? new Dictionary<int, bool>()
            : await _dbContext.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .Where(e => episodeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.Season.Show.IsPriority);

        foreach (var request in requests)
        {
            request.IsPriorityMedia = false;
            
            if (!request.MediaId.HasValue)
            {
                continue;
            }

            switch (request.MediaType)
            {
                case MediaType.Movie:
                    if (moviePriorityMap.TryGetValue(request.MediaId.Value, out var moviePriority) && moviePriority)
                    {
                        request.IsPriorityMedia = true;
                    }
                    break;

                case MediaType.Episode:
                    if (episodePriorityMap.TryGetValue(request.MediaId.Value, out var episodePriority) && episodePriority)
                    {
                        request.IsPriorityMedia = true;
                    }
                    break;
            }
        }
    }

    private async Task EnqueueTranslationJobAsync(TranslationRequest translationRequest, bool forcePriority)
    {
        var queueName = await GetQueueForTranslationRequestAsync(translationRequest, forcePriority);

        var job = Job.FromExpression<TranslationJob>(job =>
            job.Execute(translationRequest, CancellationToken.None));

        var jobId = _backgroundJobClient.Create(job, new EnqueuedState(queueName));
        await UpdateTranslationRequest(translationRequest, TranslationStatus.Pending, jobId);
        
        _logger.LogInformation(
            "Enqueued translation request {RequestId} to Hangfire queue |Green|{Queue}|/Green| (forcePriority={ForcePriority})",
            translationRequest.Id,
            queueName,
            forcePriority);
    }

    private async Task<string> GetQueueForTranslationRequestAsync(
        TranslationRequest translationRequest,
        bool forcePriority)
    {
        if (forcePriority)
        {
            return PriorityTranslationQueue;
        }

        if (!translationRequest.MediaId.HasValue)
        {
            return DefaultTranslationQueue;
        }

        try
        {
            switch (translationRequest.MediaType)
            {
                case MediaType.Movie:
                    var moviePriority = await _dbContext.Movies
                        .Where(m => m.Id == translationRequest.MediaId.Value)
                        .Select(m => m.IsPriority)
                        .FirstOrDefaultAsync();
                    return moviePriority ? PriorityTranslationQueue : DefaultTranslationQueue;

                case MediaType.Episode:
                    var showPriority = await _dbContext.Episodes
                        .Where(e => e.Id == translationRequest.MediaId.Value)
                        .Select(e => e.Season.Show.IsPriority)
                        .FirstOrDefaultAsync();
                    return showPriority ? PriorityTranslationQueue : DefaultTranslationQueue;

                default:
                    return DefaultTranslationQueue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error determining queue for translation request {RequestId}. Falling back to default queue.",
                translationRequest.Id);
            return DefaultTranslationQueue;
        }
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
            Status = TranslationStatus.InProgress
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
                return await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(arrMediaId);
            case MediaType.Movie:
                return await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(arrMediaId);
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
                var movie = await _dbContext.Movies
                    .FirstOrDefaultAsync(m => m.Id == translateAbleSubtitle.MediaId);
                return movie?.Title ?? "Unknown Movie";

            case MediaType.Episode:
                var episode = await _dbContext.Episodes
                    .Include(e => e.Season)
                    .ThenInclude(s => s.Show)
                    .FirstOrDefaultAsync(e => e.Id == translateAbleSubtitle.MediaId);

                if (episode == null)
                    return "Unknown Episode";

                // Format: "Show Title - S01E02 - Episode Title"
                return $"{episode.Season.Show.Title} - " +
                       $"S{episode.Season.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - " +
                       $"{episode.Title}";

            default:
                throw new ArgumentException($"Unsupported media type: {translateAbleSubtitle.MediaType}");
        }
    }
}
