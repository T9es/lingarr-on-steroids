using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Manages concurrent translation job limits using a SemaphoreSlim-based approach.
/// This service is registered as a singleton to maintain state across all translation jobs.
/// </summary>
public class ParallelTranslationLimiter : IParallelTranslationLimiter
{
    private readonly ILogger<ParallelTranslationLimiter> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _reconfigureLock = new(1, 1);
    
    private SemaphoreSlim _semaphore;
    private int _maxConcurrency;
    private bool _initialized;

    public ParallelTranslationLimiter(
        ILogger<ParallelTranslationLimiter> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _maxConcurrency = 1; // Default to 1 until configured
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
    }

    public int MaxConcurrency => _maxConcurrency;
    public int AvailableSlots => _semaphore.CurrentCount;

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        // Lazy initialization: load settings on first use
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        _logger.LogDebug(
            "Acquiring translation slot. Available: {Available}/{Max}",
            _semaphore.CurrentCount, _maxConcurrency);

        await _semaphore.WaitAsync(cancellationToken);

        _logger.LogDebug(
            "Translation slot acquired. Available: {Available}/{Max}",
            _semaphore.CurrentCount, _maxConcurrency);

        return new SlotReleaser(this);
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

            // Create new semaphore with updated limit
            // Note: existing waiters will continue on old semaphore until they release
            var newSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var oldSemaphore = _semaphore;
            
            _semaphore = newSemaphore;
            _maxConcurrency = maxConcurrency;

            // Dispose old semaphore (existing waiters will complete naturally)
            oldSemaphore.Dispose();
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

                _semaphore.Dispose();
                _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                _maxConcurrency = maxConcurrency;
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
        try
        {
            _semaphore.Release();
            _logger.LogDebug(
                "Translation slot released. Available: {Available}/{Max}",
                _semaphore.CurrentCount, _maxConcurrency);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore was reconfigured; this is expected
        }
    }

    /// <summary>
    /// Disposable wrapper that releases the semaphore slot when disposed.
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
