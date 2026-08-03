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
    /// Registers a translation attempt under its worker ownership token.
    /// Attempt-scoped registrations are independent, so a delayed older attempt
    /// cannot replace a newer attempt's token.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <param name="ownershipToken">The worker ownership token for this attempt</param>
    /// <returns>A CancellationToken the job should use for cancellation checks</returns>
    CancellationToken RegisterJob(int requestId, string ownershipToken);
    
    /// <summary>
    /// Gets the cancellation token for a registered job.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <returns>The CancellationToken if found, or CancellationToken.None if not registered</returns>
    CancellationToken GetToken(int requestId);

    /// <summary>
    /// Gets the cancellation token only when the registered attempt still has the expected ownership token.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <param name="expectedOwnershipToken">The worker ownership token captured with the request</param>
    /// <returns>The matching CancellationToken, or CancellationToken.None when the attempt was replaced or is not registered</returns>
    CancellationToken GetToken(int requestId, string? expectedOwnershipToken);
    
    /// <summary>
    /// Triggers cancellation for a running job.
    /// </summary>
    /// <param name="requestId">The translation request ID to cancel</param>
    /// <returns>True if the job was found and cancelled, false otherwise</returns>
    bool CancelJob(int requestId);

    /// <summary>
    /// Triggers cancellation only when the registered attempt still owns the captured cancellation token.
    /// </summary>
    /// <param name="requestId">The translation request ID to cancel</param>
    /// <param name="expectedToken">The token captured before the maintenance CAS</param>
    /// <returns>True if the captured attempt was found and cancelled, false for a replaced or missing attempt</returns>
    bool CancelJob(int requestId, CancellationToken expectedToken);
    
    /// <summary>
    /// Unregisters a job and cleans up its CancellationTokenSource.
    /// Should be called when a job completes (success, failure, or cancellation).
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    void UnregisterJob(int requestId);

    /// <summary>
    /// Unregisters a job only when the registered attempt still owns the captured cancellation token.
    /// </summary>
    /// <param name="requestId">The translation request ID</param>
    /// <param name="expectedToken">The token captured when the attempt was registered</param>
    /// <returns>True when the captured registration was removed, false when it was replaced or missing</returns>
    bool UnregisterJob(int requestId, CancellationToken expectedToken);
}
