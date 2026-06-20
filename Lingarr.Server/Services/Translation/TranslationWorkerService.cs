using System.Collections.Concurrent;
using System.Linq;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Background service that manages translation workers.
/// Replaces Hangfire for translation job processing with a custom database-polling approach.
/// </summary>
public class TranslationWorkerService : BackgroundService, ITranslationWorkerService
{
    private const int MaxWorkersLimit = 20;
    private const int MinPollIntervalMs = 500;
    private const int IdlePollIntervalMs = 5000;
    
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationWorkerService> _logger;
    private readonly SemaphoreSlim _workSignal = new(0, int.MaxValue);
    private readonly ConcurrentDictionary<int, Task> _activeWorkerTasks = new();
    private readonly ConcurrentDictionary<int, TranslationWorkloadKind> _activeWorkerKinds = new();
    
    private int _maxWorkers = 1;
    private int _reservedUploadWorkerSlots;
    private volatile bool _isInitialized;
    private TranslationWorkloadKind? _lastClaimedWorkloadKind;

    public TranslationWorkerService(
        IServiceProvider serviceProvider,
        ILogger<TranslationWorkerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public int ActiveWorkers => _activeWorkerTasks.Count;

    /// <inheritdoc />
    public int MaxWorkers => _maxWorkers;

    /// <inheritdoc />
    public Task ReconfigureWorkersAsync(int maxWorkers)
    {
        var newMax = Math.Clamp(maxWorkers, 1, MaxWorkersLimit);
        var oldMax = _maxWorkers;
        _maxWorkers = newMax;
        _reservedUploadWorkerSlots = ClampReservedUploadSlots(_reservedUploadWorkerSlots);
        
        _logger.LogInformation(
            "Translation worker count reconfigured from {Old} to {New} (active: {Active}, reserved upload slots: {ReservedUploadSlots})",
            oldMax, newMax, ActiveWorkers, _reservedUploadWorkerSlots);
        
        // Signal to potentially spawn more workers
        if (newMax > oldMax)
        {
            Signal();
        }
        
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReconfigureReservedUploadSlotsAsync(int reservedUploadSlots)
    {
        var requested = Math.Max(0, reservedUploadSlots);
        var clamped = ClampReservedUploadSlots(requested);
        var oldReserved = _reservedUploadWorkerSlots;
        _reservedUploadWorkerSlots = clamped;

        if (requested != clamped)
        {
            _logger.LogWarning(
                "Reserved upload worker slots value {RequestedSlots} was clamped to {ClampedSlots} for max worker count {MaxWorkers}.",
                requested,
                clamped,
                _maxWorkers);
        }

        _logger.LogInformation(
            "Reserved upload worker slots reconfigured from {Old} to {New} (max workers: {MaxWorkers}, active: {Active})",
            oldReserved,
            clamped,
            _maxWorkers,
            ActiveWorkers);

        Signal();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Signal()
    {
        try
        {
            _workSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled, safe to ignore
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TranslationWorkerService starting...");
        
        try
        {
            // Initialize max workers from settings
            await InitializeAsync(stoppingToken);
            
            // Recovery: Mark InProgress jobs as Pending (they were interrupted by restart)
            await RecoverInterruptedJobsAsync(stoppingToken);
            
            _logger.LogInformation(
                "TranslationWorkerService started with {MaxWorkers} max workers",
                _maxWorkers);
            
            // Schedule a delayed re-queue to work around jobs hanging on startup
            // Some jobs claimed immediately after restart seem to hang silently.
            // Re-enqueueing after a short delay resets them and they process normally.
            _ = ScheduleDelayedRequeueAsync(stoppingToken);
            
            // Main worker management loop
            await RunWorkerLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("TranslationWorkerService shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranslationWorkerService encountered a fatal error");
            throw;
        }
        finally
        {
            // Wait for active workers to complete gracefully
            await WaitForActiveWorkersAsync();
        }
    }
    
    private async Task ScheduleDelayedRequeueAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait 5 seconds after startup before re-queueing
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            
            if (stoppingToken.IsCancellationRequested) return;
            
            _logger.LogInformation("Performing delayed re-queue to reset any hung jobs from startup...");
            
            using var scope = _serviceProvider.CreateScope();
            var translationRequestService = scope.ServiceProvider.GetRequiredService<ITranslationRequestService>();
            
            // Re-enqueue only pending items, don't touch in-progress (they'll be cancelled and reset)
            var result = await translationRequestService.ReenqueueQueuedRequests(includeInProgress: true);
            
            if (result.Reenqueued > 0 || result.SkippedProcessing > 0)
            {
                _logger.LogInformation(
                    "Delayed re-queue complete: {Reenqueued} re-enqueued, {Skipped} skipped (in-progress)",
                    result.Reenqueued, result.SkippedProcessing);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested, ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delayed re-queue failed (non-fatal)");
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
        
        var setting = await settingService.GetSetting(SettingKeys.Translation.MaxParallelTranslations);
        var maxWorkers = int.TryParse(setting, out var value) && value > 0 
            ? Math.Clamp(value, 1, MaxWorkersLimit) 
            : 1;

        var reservedUploadSetting = await settingService.GetSetting(SettingKeys.UploadWorkspace.ReservedWorkerSlots);
        var requestedReservedUploadSlots = int.TryParse(reservedUploadSetting, out var reservedValue) && reservedValue >= 0
            ? reservedValue
            : 0;
        
        _maxWorkers = maxWorkers;
        _reservedUploadWorkerSlots = ClampReservedUploadSlots(requestedReservedUploadSlots);

        if (_reservedUploadWorkerSlots != requestedReservedUploadSlots)
        {
            _logger.LogWarning(
                "Reserved upload worker slots value {RequestedSlots} was clamped to {ClampedSlots} for max worker count {MaxWorkers}.",
                requestedReservedUploadSlots,
                _reservedUploadWorkerSlots,
                _maxWorkers);
        }

        _isInitialized = true;
        
        _logger.LogInformation(
            "Initialized with max {MaxWorkers} workers and {ReservedUploadSlots} reserved upload slot(s)",
            _maxWorkers,
            _reservedUploadWorkerSlots);
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        
        // Find jobs that were InProgress when the application stopped
        var interruptedCount = await dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.InProgress)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TranslationStatus.Pending),
                cancellationToken);
        
        if (interruptedCount > 0)
        {
            _logger.LogInformation(
                "Recovered {Count} interrupted translation request(s) - reset to Pending",
                interruptedCount);
        }
    }

    private int ClampReservedUploadSlots(int requestedSlots)
    {
        if (_maxWorkers <= 1)
        {
            return 0;
        }

        return Math.Clamp(requestedSlots, 0, _maxWorkers - 1);
    }

    private int GetMaxNonUploadWorkersWhenContended()
    {
        var maxNonUpload = _maxWorkers - _reservedUploadWorkerSlots;
        return maxNonUpload <= 0 ? 1 : maxNonUpload;
    }

    private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Clean up completed worker tasks
            CleanupCompletedWorkers();
            
            // Spawn new workers if under limit and work is available
            var spawned = 0;
            while (ActiveWorkers < _maxWorkers && !stoppingToken.IsCancellationRequested)
            {
                var claimed = await TryClaimAndStartWorkerAsync(stoppingToken);
                if (!claimed) break;
                spawned++;
            }
            
            // Determine wait strategy based on current state
            if (ActiveWorkers > 0 || spawned > 0)
            {
                // Workers are running or we just spawned some - poll frequently
                await WaitForWorkOrTimeoutAsync(MinPollIntervalMs, stoppingToken);
            }
            else
            {
                // No workers running and no work found - poll less frequently
                await WaitForWorkOrTimeoutAsync(IdlePollIntervalMs, stoppingToken);
            }
        }
    }

    private void CleanupCompletedWorkers()
    {
        var completedIds = _activeWorkerTasks
            .Where(kv => kv.Value.IsCompleted)
            .Select(kv => kv.Key)
            .ToList();
        
        foreach (var id in completedIds)
        {
            if (_activeWorkerTasks.TryRemove(id, out var task))
            {
                _activeWorkerKinds.TryRemove(id, out _);

                // Log if task faulted
                if (task.IsFaulted)
                {
                    _logger.LogError(
                        task.Exception,
                        "Worker task for request {RequestId} faulted",
                        id);
                }
            }
        }
    }

    private async Task<bool> TryClaimAndStartWorkerAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var activeUploadWorkers = _activeWorkerKinds.Values.Count(kind => kind == TranslationWorkloadKind.Upload);
        var activeNonUploadWorkers = ActiveWorkers - activeUploadWorkers;

        var hasPendingUpload = await dbContext.TranslationRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Status == TranslationStatus.Pending &&
                           request.WorkloadKind == TranslationWorkloadKind.Upload,
                stoppingToken);
        var hasPendingNonUpload = await dbContext.TranslationRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Status == TranslationStatus.Pending &&
                           request.WorkloadKind != TranslationWorkloadKind.Upload,
                stoppingToken);

        if (!hasPendingUpload && !hasPendingNonUpload)
        {
            return false;
        }

        var maxNonUploadWorkersWhenContended = GetMaxNonUploadWorkersWhenContended();
        var maxUploadWorkersWhenContended = _maxWorkers - maxNonUploadWorkersWhenContended;

        bool? claimUploadWork = false;
        if (hasPendingUpload && hasPendingNonUpload)
        {
            if (_reservedUploadWorkerSlots == 0)
            {
                if (activeUploadWorkers == 0 && activeNonUploadWorkers > 0)
                {
                    claimUploadWork = true;
                }
                else if (activeNonUploadWorkers == 0 && activeUploadWorkers > 0)
                {
                    claimUploadWork = false;
                }
                else if (ActiveWorkers == 0)
                {
                    claimUploadWork = _lastClaimedWorkloadKind != TranslationWorkloadKind.Upload;
                }
                else
                {
                    claimUploadWork = null;
                }
            }
            else if (activeNonUploadWorkers < maxNonUploadWorkersWhenContended)
            {
                claimUploadWork = false;
            }
            else if (activeUploadWorkers < maxUploadWorkersWhenContended)
            {
                claimUploadWork = true;
            }
            else
            {
                return false;
            }
        }
        else if (hasPendingUpload)
        {
            claimUploadWork = true;
        }

        var candidate = await dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.Status == TranslationStatus.Pending)
            .Where(request => !claimUploadWork.HasValue ||
                (claimUploadWork.Value
                    ? request.WorkloadKind == TranslationWorkloadKind.Upload
                    : request.WorkloadKind != TranslationWorkloadKind.Upload))
            .OrderByEffectiveQueuePriority(dbContext)
            .Select(request => new { request.Id, request.WorkloadKind })
            .FirstOrDefaultAsync(stoppingToken);

        if (candidate == null)
        {
            return false;
        }

        var claimed = await dbContext.TranslationRequests
            .Where(request => request.Id == candidate.Id && request.Status == TranslationStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(request => request.Status, TranslationStatus.InProgress),
                stoppingToken);

        if (claimed == 0)
        {
            _logger.LogDebug("Request {RequestId} was claimed by another worker", candidate.Id);
            return true;
        }

        try
        {
            var translationRequestService = scope.ServiceProvider.GetRequiredService<ITranslationRequestService>();
            await translationRequestService.UpdateActiveCount();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status update for request {RequestId}", candidate.Id);
        }

        _logger.LogInformation(
            "Claimed translation request {RequestId} ({WorkloadKind}) - starting worker (active: {Active}/{Max}, upload active/reserved: {UploadActive}/{UploadReserved})",
            candidate.Id,
            candidate.WorkloadKind,
            ActiveWorkers + 1,
            _maxWorkers,
            activeUploadWorkers + (candidate.WorkloadKind == TranslationWorkloadKind.Upload ? 1 : 0),
            maxUploadWorkersWhenContended);

        var workerTask = ProcessRequestAsync(candidate.Id, stoppingToken);
        _lastClaimedWorkloadKind = candidate.WorkloadKind;
        _activeWorkerTasks.TryAdd(candidate.Id, workerTask);
        _activeWorkerKinds.TryAdd(candidate.Id, candidate.WorkloadKind);

        return true;
    }

    private async Task ProcessRequestAsync(int requestId, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _serviceProvider.CreateScope();
                var translationJob = scope.ServiceProvider.GetRequiredService<TranslationJob>();

                await translationJob.ExecuteAsync(requestId, stoppingToken);

                // Check if the request was paused (e.g. Gemini 429 rate limit)
                var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
                var request = await dbContext.TranslationRequests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == requestId, stoppingToken);

                if (request?.Status != TranslationStatus.Paused || request.NextRetryAt == null)
                    break;

                // Hold the slot: wait until the resume time
                var delay = request.NextRetryAt.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogInformation(
                        "Worker holding slot for paused request {RequestId}, waiting {Delay:g} before retry",
                        requestId, delay);
                    await Task.Delay(delay, stoppingToken);
                }

                // Resume: set back to InProgress directly
                // (UpdateTranslationRequest blocks Paused to InProgress)
                await dbContext.TranslationRequests
                    .Where(r => r.Id == requestId && r.Status == TranslationStatus.Paused)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, TranslationStatus.InProgress)
                        .SetProperty(r => r.PausedAt, (DateTime?)null)
                        .SetProperty(r => r.PauseReason, (string?)null)
                        .SetProperty(r => r.PausedProvider, (string?)null)
                        .SetProperty(r => r.NextRetryAt, (DateTime?)null),
                        stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Translation request {RequestId} was cancelled due to shutdown",
                requestId);
            
            // Reset to Pending so it can be picked up after restart
            await ResetRequestToPendingAsync(requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing translation request {RequestId}", requestId);
            
            // If we failed before TranslationJob could handle it (e.g., DI failure),
            // we must mark the job as Failed to prevent infinite retry loops.
            // The recovery logic resets InProgress→Pending on startup, so leaving
            // a job in InProgress would cause it to be retried endlessly.
            await MarkRequestAsFailedAsync(requestId, ex.Message);
        }
        finally
        {
            _activeWorkerTasks.TryRemove(requestId, out _);
            _activeWorkerKinds.TryRemove(requestId, out _);
        }
    }

    private async Task ResetRequestToPendingAsync(int requestId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
            
            await dbContext.TranslationRequests
                .Where(r => r.Id == requestId && r.Status == TranslationStatus.InProgress)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, TranslationStatus.Pending));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset request {RequestId} to Pending", requestId);
        }
    }

    private async Task MarkRequestAsFailedAsync(int requestId, string errorMessage)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
            
            var now = DateTime.UtcNow;
            
            // Only update if still InProgress - TranslationJob may have already marked it Failed
            var rowsUpdated = await dbContext.TranslationRequests
                .Where(r => r.Id == requestId && r.Status == TranslationStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, TranslationStatus.Failed)
                    .SetProperty(r => r.IsActive, (bool?)null)
                    .SetProperty(r => r.CompletedAt, now));
            
            // Only add log entry if we actually changed the status
            // (avoids duplicate logs when TranslationJob already handled the failure)
            if (rowsUpdated > 0)
            {
                dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
                {
                    TranslationRequestId = requestId,
                    Level = "Error",
                    Message = "Worker service failed to process request",
                    Details = errorMessage
                });
                await dbContext.SaveChangesAsync();
                
                _logger.LogWarning(
                    "Marked request {RequestId} as Failed due to worker error: {Error}",
                    requestId, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark request {RequestId} as Failed", requestId);
        }
    }

    private async Task WaitForWorkOrTimeoutAsync(int timeoutMs, CancellationToken stoppingToken)
    {
        try
        {
            await _workSignal.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested, propagate
            throw;
        }
    }

    private async Task WaitForActiveWorkersAsync()
    {
        var activeTasks = _activeWorkerTasks.Values.ToList();
        if (activeTasks.Count == 0) return;
        
        _logger.LogInformation(
            "Waiting for {Count} active worker(s) to complete...",
            activeTasks.Count);
        
        try
        {
            // Give workers a reasonable time to finish gracefully
            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var allTasks = Task.WhenAll(activeTasks);
            
            var completed = await Task.WhenAny(allTasks, timeout);
            
            if (completed == timeout)
            {
                _logger.LogWarning(
                    "Timeout waiting for workers - {Count} worker(s) still running",
                    _activeWorkerTasks.Count);
            }
            else
            {
                _logger.LogInformation("All workers completed gracefully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error waiting for workers to complete");
        }
    }
}
