namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Manages cancellation tokens for translation jobs, enabling cooperative cancellation
/// of running jobs when cancelled from the UI.
/// </summary>
public interface ITranslationCancellationService
{
    /// <summary>
    /// Registers a translation job and creates a CancellationTokenSource for it.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <returns>A CancellationToken the job should use for cancellation checks</returns>
    CancellationToken RegisterJob(int requestId);
    
    /// <summary>
    /// Gets the cancellation token for a registered job.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <returns>The CancellationToken if found, or CancellationToken.None if not registered</returns>
    CancellationToken GetToken(int requestId);
    
    /// <summary>
    /// Triggers cancellation for a running job.
    /// </summary>
    /// <param name="requestId">The translation request ID to cancel</param>
    /// <returns>True if the job was found and cancelled, false otherwise</returns>
    bool CancelJob(int requestId);
    
    /// <summary>
    /// Unregisters a job and cleans up its CancellationTokenSource.
    /// Should be called when a job completes (success, failure, or cancellation).
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    void UnregisterJob(int requestId);
}
