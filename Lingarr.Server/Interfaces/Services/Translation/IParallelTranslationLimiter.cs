using Lingarr.Core.Enum;

namespace Lingarr.Server.Interfaces.Services.Translation;

/// <summary>
/// Manages concurrent translation job limits using a priority-aware waiting mechanism.
/// Priority jobs are processed before non-priority jobs when a slot becomes available.
/// </summary>
public interface IParallelTranslationLimiter
{
    /// <summary>
    /// Acquires a slot for translation. Blocks if the limit is reached.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An IDisposable that releases the slot when disposed</returns>
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Acquires a slot for translation with priority support.
    /// Priority requests are processed before non-priority requests when a slot becomes available.
    /// </summary>
    /// <param name="isPriority">Whether this is a priority request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An IDisposable that releases the slot when disposed</returns>
    Task<IDisposable> AcquireAsync(bool isPriority, CancellationToken cancellationToken);

    /// <summary>
    /// Acquires a slot for a translation request, automatically looking up priority from DB.
    /// This is the preferred method for the unified queue system - priority is determined
    /// at acquire time, ensuring priority changes take effect immediately.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <param name="mediaType">The media type (Movie or Episode)</param>
    /// <param name="mediaId">The media ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An IDisposable that releases the slot when disposed</returns>
    Task<IDisposable> AcquireForRequestAsync(
        int requestId, 
        MediaType mediaType, 
        int? mediaId, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Notifies the limiter that a media item's priority has changed.
    /// Any waiting jobs for this media will be reordered in the queue.
    /// </summary>
    /// <param name="mediaType">The type of media (Movie, Show)</param>
    /// <param name="mediaId">The media ID</param>
    void NotifyPriorityChanged(MediaType mediaType, int mediaId);

    /// <summary>
    /// Reconfigures the maximum concurrency limit.
    /// </summary>
    /// <param name="maxConcurrency">New maximum concurrent translations</param>
    Task ReconfigureAsync(int maxConcurrency);

    /// <summary>
    /// Gets the current maximum concurrency setting.
    /// </summary>
    int MaxConcurrency { get; }

    /// <summary>
    /// Gets the number of available slots.
    /// </summary>
    int AvailableSlots { get; }
}
