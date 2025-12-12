using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Manages concurrent translation job limits using a priority-aware waiting mechanism.
/// Priority jobs are processed before non-priority jobs when a slot becomes available.
/// This service is registered as a singleton to maintain state across all translation jobs.
/// </summary>
public class ParallelTranslationLimiter : IParallelTranslationLimiter
{
    private readonly ILogger<ParallelTranslationLimiter> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _reconfigureLock = new(1, 1);
    
    // Priority queue for waiters - priority jobs go to front, non-priority to back
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

        _logger.LogDebug(
            "Acquiring translation slot (priority={IsPriority}). Available: {Available}/{Max}, Waiting: {Waiting}",
            isPriority, _currentSlots, _maxConcurrency, _waiters.Count);

        TaskCompletionSource<bool>? tcs = null;
        CancellationTokenRegistration? registration = null;

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
            var entry = new WaiterEntry(isPriority, tcs);

            // Priority jobs go to the front, non-priority to the back
            if (isPriority)
            {
                // Insert at front, but after any existing priority waiters
                var node = _waiters.First;
                while (node != null && node.Value.IsPriority)
                {
                    node = node.Next;
                }
                if (node == null)
                {
                    _waiters.AddLast(entry);
                }
                else
                {
                    _waiters.AddBefore(node, entry);
                }
            }
            else
            {
                _waiters.AddLast(entry);
            }

            // Register cancellation to remove from queue
            registration = cancellationToken.Register(() =>
            {
                lock (_lock)
                {
                    if (_waiters.Remove(entry))
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                }
            });
        }

        try
        {
            await tcs.Task;
            registration?.Dispose();
            
            _logger.LogDebug(
                "Translation slot acquired after waiting. Available: {Available}/{Max}",
                _currentSlots, _maxConcurrency);
            
            return new SlotReleaser(this);
        }
        catch (OperationCanceledException)
        {
            registration?.Dispose();
            throw;
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

                // If we have more slots now, wake up waiting jobs
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
            // If someone is waiting, give them the slot
            if (_waiters.Count > 0)
            {
                var first = _waiters.First!;
                _waiters.RemoveFirst();
                first.Value.CompletionSource.TrySetResult(true);
                
                _logger.LogDebug(
                    "Translation slot released and given to waiter (priority={IsPriority}). Waiting: {Waiting}",
                    first.Value.IsPriority, _waiters.Count);
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

    private record WaiterEntry(bool IsPriority, TaskCompletionSource<bool> CompletionSource);

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
