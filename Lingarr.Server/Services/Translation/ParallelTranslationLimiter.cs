using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Manages concurrent translation job limits using a priority-aware waiting mechanism.
/// Priority jobs are processed before non-priority jobs when a slot becomes available.
/// 
/// Key features:
/// - Looks up priority from the database at acquire time (not just at enqueue time)
/// - Reorders waiting jobs when priority changes via NotifyPriorityChanged()
/// - Uses a linked list for O(n) reordering when priorities change
/// 
/// This service is registered as a singleton to maintain state across all translation jobs.
/// </summary>
public class ParallelTranslationLimiter : IParallelTranslationLimiter
{
    private readonly ILogger<ParallelTranslationLimiter> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _reconfigureLock = new(1, 1);
    
    // Waiting jobs, ordered by priority. Priority jobs at front, then by creation order.
    private readonly LinkedList<WaiterEntry> _waiters = new();
    
    private int _maxConcurrency;
    private int _currentSlots; // Available slots
    private bool _initialized;

    public ParallelTranslationLimiter(
        ILogger<ParallelTranslationLimiter> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _maxConcurrency = 1; // Default to 1 until configured
        _currentSlots = _maxConcurrency;
    }

    public int MaxConcurrency => _maxConcurrency;
    public int AvailableSlots => _currentSlots;

    /// <inheritdoc />
    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        return AcquireAsync(false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(bool isPriority, CancellationToken cancellationToken)
    {
        // Lazy initialization: load settings on first use
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        return await AcquireInternalAsync(isPriority, null, null, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDisposable> AcquireForRequestAsync(
        int requestId, 
        MediaType mediaType, 
        int? mediaId, 
        CancellationToken cancellationToken)
    {
        // Lazy initialization: load settings on first use
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        // Look up current priority and ShowId (for episodes) from database
        var (isPriority, showId) = await LookupPriorityAndShowIdAsync(mediaType, mediaId);
        
        _logger.LogDebug(
            "Request {RequestId} priority lookup: MediaType={MediaType}, MediaId={MediaId}, IsPriority={IsPriority}, ShowId={ShowId}",
            requestId, mediaType, mediaId, isPriority, showId);

        return await AcquireInternalAsync(isPriority, requestId, mediaType, mediaId, showId, cancellationToken);
    }

    /// <inheritdoc />
    public void NotifyPriorityChanged(MediaType mediaType, int mediaId)
    {
        // This runs synchronously but we need to do an async DB lookup.
        // We'll fire-and-forget a background task to handle the reordering.
        _ = NotifyPriorityChangedInternalAsync(mediaType, mediaId);
    }

    private async Task NotifyPriorityChangedInternalAsync(MediaType mediaType, int mediaId)
    {
        try
        {
            // Look up the NEW priority state from the database
            bool newPriorityState;
            int? showIdToMatch = null;

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

                switch (mediaType)
                {
                    case MediaType.Movie:
                        newPriorityState = await dbContext.Movies
                            .Where(m => m.Id == mediaId)
                            .Select(m => m.IsPriority)
                            .FirstOrDefaultAsync();
                        break;

                    case MediaType.Show:
                        // Priority changed on a show - get the show's priority
                        // We'll match episodes by their ShowId
                        newPriorityState = await dbContext.Shows
                            .Where(s => s.Id == mediaId)
                            .Select(s => s.IsPriority)
                            .FirstOrDefaultAsync();
                        showIdToMatch = mediaId;
                        break;

                    default:
                        _logger.LogWarning(
                            "NotifyPriorityChanged: Unsupported media type {MediaType}",
                            mediaType);
                        return;
                }
            }

            lock (_lock)
            {
                var reorderedCount = 0;
                var nodesToReorder = new List<(LinkedListNode<WaiterEntry> Node, bool NewPriority)>();

                // Find all waiters that should be affected
                var node = _waiters.First;
                while (node != null)
                {
                    var entry = node.Value;
                    var shouldReorder = false;

                    if (mediaType == MediaType.Movie)
                    {
                        // For movies, match by MovieId
                        if (entry.MediaType == MediaType.Movie && entry.MediaId == mediaId)
                        {
                            shouldReorder = true;
                        }
                    }
                    else if (mediaType == MediaType.Show)
                    {
                        // For shows, match episodes by ShowId
                        if (entry.MediaType == MediaType.Episode && entry.ShowId == showIdToMatch)
                        {
                            shouldReorder = true;
                        }
                    }

                    if (shouldReorder && entry.IsPriority != newPriorityState)
                    {
                        nodesToReorder.Add((node, newPriorityState));
                    }

                    node = node.Next;
                }

                if (nodesToReorder.Count == 0)
                {
                    _logger.LogDebug(
                        "NotifyPriorityChanged: No waiting jobs need reordering for {MediaType} {MediaId}",
                        mediaType, mediaId);
                    return;
                }

                // Reorder all affected nodes
                foreach (var (nodeToMove, newPriority) in nodesToReorder)
                {
                    var entry = nodeToMove.Value;
                    _waiters.Remove(nodeToMove);

                    // Re-insert at correct position with updated priority
                    var newEntry = entry with { IsPriority = newPriority };
                    InsertWaiterInOrder(newEntry);
                    reorderedCount++;
                }

                _logger.LogInformation(
                    "Priority changed for {MediaType} {MediaId}: Reordered {Count} waiting job(s), new priority={NewPriority}",
                    mediaType, mediaId, reorderedCount, newPriorityState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in NotifyPriorityChanged for {MediaType} {MediaId}",
                mediaType, mediaId);
        }
    }

    /// <inheritdoc />
    public async Task ReconfigureAsync(int maxConcurrency)
    {
        if (maxConcurrency < 1)
        {
            maxConcurrency = 1;
        }

        await _reconfigureLock.WaitAsync();
        try
        {
            if (_maxConcurrency == maxConcurrency)
            {
                return;
            }

            _logger.LogInformation(
                "Reconfiguring parallel translation limit from {Old} to {New}",
                _maxConcurrency, maxConcurrency);

            lock (_lock)
            {
                var oldMax = _maxConcurrency;
                _maxConcurrency = maxConcurrency;
                
                // Adjust available slots
                var diff = maxConcurrency - oldMax;
                _currentSlots = Math.Max(0, _currentSlots + diff);

                // If we have more slots now, wake up waiting jobs in priority order
                while (_currentSlots > 0 && _waiters.Count > 0)
                {
                    var first = _waiters.First!;
                    _waiters.RemoveFirst();
                    _currentSlots--;
                    first.Value.CompletionSource.TrySetResult(true);
                }
            }
        }
        finally
        {
            _reconfigureLock.Release();
        }
    }

    private async Task<IDisposable> AcquireInternalAsync(
        bool isPriority,
        int? requestId,
        MediaType? mediaType,
        int? mediaId,
        int? showId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Acquiring translation slot (priority={IsPriority}, request={RequestId}). Available: {Available}/{Max}, Waiting: {Waiting}",
            isPriority, requestId, _currentSlots, _maxConcurrency, _waiters.Count);

        TaskCompletionSource<bool>? tcs = null;
        CancellationTokenRegistration? registration = null;
        WaiterEntry? entry = null;

        lock (_lock)
        {
            // If slot is immediately available and no one is waiting, take it
            if (_currentSlots > 0 && _waiters.Count == 0)
            {
                _currentSlots--;
                _logger.LogDebug(
                    "Translation slot acquired immediately. Available: {Available}/{Max}",
                    _currentSlots, _maxConcurrency);
                return new SlotReleaser(this);
            }

            // Need to wait for a slot - create a waiter entry
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entry = new WaiterEntry(
                isPriority, 
                tcs, 
                requestId, 
                mediaType ?? MediaType.Movie, 
                mediaId,
                showId);

            InsertWaiterInOrder(entry);

            // Register cancellation to remove from queue
            registration = cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    // Find and remove this entry
                    var node = _waiters.First;
                    while (node != null)
                    {
                        if (ReferenceEquals(node.Value, entry))
                        {
                            _waiters.Remove(node);
                            tcs.TrySetCanceled(cancellationToken);
                            break;
                        }
                        node = node.Next;
                    }
                }
            });
        }

        try
        {
            await tcs.Task;
            registration?.Dispose();
            
            _logger.LogDebug(
                "Translation slot acquired after waiting (request={RequestId}). Available: {Available}/{Max}",
                requestId, _currentSlots, _maxConcurrency);
            
            return new SlotReleaser(this);
        }
        catch (OperationCanceledException)
        {
            registration?.Dispose();
            throw;
        }
    }

    private void InsertWaiterInOrder(WaiterEntry entry)
    {
        // Priority jobs go before non-priority jobs
        // Within same priority level, maintain FIFO order (add to end of that priority group)
        
        if (entry.IsPriority)
        {
            // Find the last priority entry and insert after it (or at front if none)
            var node = _waiters.First;
            LinkedListNode<WaiterEntry>? lastPriorityNode = null;
            
            while (node != null && node.Value.IsPriority)
            {
                lastPriorityNode = node;
                node = node.Next;
            }
            
            if (lastPriorityNode == null)
            {
                _waiters.AddFirst(entry);
            }
            else
            {
                _waiters.AddAfter(lastPriorityNode, entry);
            }
        }
        else
        {
            // Non-priority: add to the end
            _waiters.AddLast(entry);
        }
    }

    private async Task<(bool IsPriority, int? ShowId)> LookupPriorityAndShowIdAsync(MediaType mediaType, int? mediaId)
    {
        if (!mediaId.HasValue)
        {
            return (false, null);
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

            switch (mediaType)
            {
                case MediaType.Movie:
                    var moviePriority = await dbContext.Movies
                        .Where(m => m.Id == mediaId.Value)
                        .Select(m => m.IsPriority)
                        .FirstOrDefaultAsync();
                    return (moviePriority, null);

                case MediaType.Episode:
                    // For episodes, we need both priority AND the ShowId for later matching
                    var episodeInfo = await dbContext.Episodes
                        .Where(e => e.Id == mediaId.Value)
                        .Select(e => new { e.Season.Show.IsPriority, ShowId = e.Season.ShowId })
                        .FirstOrDefaultAsync();
                    
                    return episodeInfo != null 
                        ? (episodeInfo.IsPriority, episodeInfo.ShowId) 
                        : (false, null);

                default:
                    return (false, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Error looking up priority for {MediaType} {MediaId}. Defaulting to non-priority.",
                mediaType, mediaId);
            return (false, null);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _reconfigureLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            using var scope = _serviceProvider.CreateScope();
            var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
            
            var setting = await settingService.GetSetting(SettingKeys.Translation.MaxParallelTranslations);
            var maxConcurrency = int.TryParse(setting, out var value) && value > 0 ? value : 1;

            if (maxConcurrency != _maxConcurrency)
            {
                _logger.LogInformation(
                    "Initializing parallel translation limit to {Max}",
                    maxConcurrency);

                lock (_lock)
                {
                    _maxConcurrency = maxConcurrency;
                    _currentSlots = maxConcurrency;
                }
            }

            _initialized = true;
        }
        finally
        {
            _reconfigureLock.Release();
        }
    }

    private void Release()
    {
        lock (_lock)
        {
            // If someone is waiting, give them the slot (first in queue = highest priority)
            if (_waiters.Count > 0)
            {
                var first = _waiters.First!;
                _waiters.RemoveFirst();
                first.Value.CompletionSource.TrySetResult(true);
                
                _logger.LogDebug(
                    "Translation slot released and given to waiter (priority={IsPriority}, request={RequestId}). Waiting: {Waiting}",
                    first.Value.IsPriority, first.Value.RequestId, _waiters.Count);
            }
            else
            {
                // No waiters, return slot to pool
                _currentSlots++;
                _logger.LogDebug(
                    "Translation slot released. Available: {Available}/{Max}",
                    _currentSlots, _maxConcurrency);
            }
        }
    }

    /// <summary>
    /// Represents a job waiting for a translation slot.
    /// </summary>
    private record WaiterEntry(
        bool IsPriority, 
        TaskCompletionSource<bool> CompletionSource,
        int? RequestId,
        MediaType MediaType,
        int? MediaId,
        int? ShowId);

    /// <summary>
    /// Disposable wrapper that releases the slot when disposed.
    /// </summary>
    private class SlotReleaser : IDisposable
    {
        private readonly ParallelTranslationLimiter _limiter;
        private bool _disposed;

        public SlotReleaser(ParallelTranslationLimiter limiter)
        {
            _limiter = limiter;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _limiter.Release();
        }
    }
}
