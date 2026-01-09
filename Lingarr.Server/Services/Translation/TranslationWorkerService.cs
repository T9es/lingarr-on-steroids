using System.Collections.Concurrent;
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
    
    private int _maxWorkers = 1;
    private volatile bool _isInitialized;

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
        
        _logger.LogInformation(
            "Translation worker count reconfigured from {Old} to {New} (active: {Active})",
            oldMax, newMax, ActiveWorkers);
        
        // Signal to potentially spawn more workers
        if (newMax > oldMax)
        {
            Signal();
        }
        
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

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
        
        var setting = await settingService.GetSetting(SettingKeys.Translation.MaxParallelTranslations);
        var maxWorkers = int.TryParse(setting, out var value) && value > 0 
            ? Math.Clamp(value, 1, MaxWorkersLimit) 
            : 1;
        
        _maxWorkers = maxWorkers;
        _isInitialized = true;
        
        _logger.LogInformation("Initialized with max {MaxWorkers} workers", _maxWorkers);
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
        
        // Step 1: Find the next pending request (priority first, then oldest)
        var candidate = await dbContext.TranslationRequests
            .AsNoTracking()
            .Where(r => r.Status == TranslationStatus.Pending)
            .OrderByDescending(r => r.IsPriority)
            .ThenBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(stoppingToken);
        
        if (candidate == 0)
        {
            return false; // No pending work
        }
        
        // Step 2: Atomically claim it (optimistic lock pattern)
        // Only update if status is still Pending (prevents race conditions)
        var claimed = await dbContext.TranslationRequests
            .Where(r => r.Id == candidate && r.Status == TranslationStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, TranslationStatus.InProgress),
                stoppingToken);
        
        if (claimed == 0)
        {
            // Another worker claimed it between our SELECT and UPDATE
            _logger.LogDebug("Request {RequestId} was claimed by another worker", candidate);
            return true; // Return true to try the next one
        }
        
        // Step 3: Broadcast status change to frontend via SignalR
        // This ensures the UI updates from Pending to InProgress
        try
        {
            var translationRequestService = scope.ServiceProvider.GetRequiredService<ITranslationRequestService>();
            await translationRequestService.UpdateActiveCount();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status update for request {RequestId}", candidate);
            // Continue anyway - the job should still run
        }
        
        // Step 4: Start a worker task for this request
        _logger.LogInformation(
            "Claimed translation request {RequestId} - starting worker (active: {Active}/{Max})",
            candidate, ActiveWorkers + 1, _maxWorkers);
        
        var workerTask = ProcessRequestAsync(candidate, stoppingToken);
        _activeWorkerTasks.TryAdd(candidate, workerTask);
        
        return true;
    }

    private async Task ProcessRequestAsync(int requestId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var translationJob = scope.ServiceProvider.GetRequiredService<TranslationJob>();
            
            // Execute the translation job
            await translationJob.ExecuteAsync(requestId, stoppingToken);
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
