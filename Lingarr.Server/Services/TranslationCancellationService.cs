using System.Collections.Concurrent;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services;

/// <summary>
/// Singleton service that maintains cancellation tokens for running translation jobs,
/// enabling cooperative cancellation when jobs are cancelled from the UI.
/// </summary>
public class TranslationCancellationService : ITranslationCancellationService
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _tokens = new();
    private readonly ILogger<TranslationCancellationService> _logger;

    public TranslationCancellationService(ILogger<TranslationCancellationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CancellationToken RegisterJob(int requestId)
    {
        var cts = new CancellationTokenSource();
        
        if (_tokens.TryAdd(requestId, cts))
        {
            _logger.LogDebug("Registered cancellation token for request {RequestId}", requestId);
            return cts.Token;
        }
        
        // If already registered (shouldn't happen), return existing token
        cts.Dispose();
        return _tokens.TryGetValue(requestId, out var existing) 
            ? existing.Token 
            : CancellationToken.None;
    }

    /// <inheritdoc />
    public CancellationToken GetToken(int requestId)
    {
        return _tokens.TryGetValue(requestId, out var cts) 
            ? cts.Token 
            : CancellationToken.None;
    }

    /// <inheritdoc />
    public bool CancelJob(int requestId)
    {
        if (_tokens.TryGetValue(requestId, out var cts))
        {
            try
            {
                cts.Cancel();
                _logger.LogInformation("Triggered cancellation for request {RequestId}", requestId);
                return true;
            }
            catch (ObjectDisposedException)
            {
                // Token was already disposed (job finished), safe to ignore
                _logger.LogDebug("Cancellation token for request {RequestId} was already disposed", requestId);
            }
        }
        
        return false;
    }

    /// <inheritdoc />
    public void UnregisterJob(int requestId)
    {
        if (_tokens.TryRemove(requestId, out var cts))
        {
            try
            {
                cts.Dispose();
                _logger.LogDebug("Unregistered cancellation token for request {RequestId}", requestId);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, safe to ignore
            }
        }
    }
}
