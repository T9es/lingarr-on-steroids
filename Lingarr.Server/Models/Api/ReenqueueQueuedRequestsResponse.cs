namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for re-enqueue queued translation requests endpoint.
/// </summary>
public class ReenqueueQueuedRequestsResponse
{
    /// <summary>
    /// Number of duplicate translation requests that were removed before re-enqueueing.
    /// </summary>
    public int RemovedDuplicates { get; set; }

    /// <summary>
    /// Number of duplicate requests skipped because their Hangfire job was already processing.
    /// </summary>
    public int SkippedDuplicateProcessing { get; set; }

    /// <summary>
    /// Number of translation requests that were re-enqueued.
    /// </summary>
    public int Reenqueued { get; set; }

    /// <summary>
    /// Number of requests skipped because their Hangfire job was already processing.
    /// </summary>
    public int SkippedProcessing { get; set; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
