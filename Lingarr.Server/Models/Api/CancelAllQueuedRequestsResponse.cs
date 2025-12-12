namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for cancel all queued translation requests endpoint.
/// </summary>
public class CancelAllQueuedRequestsResponse
{
    /// <summary>
    /// Number of translation requests that were cancelled.
    /// </summary>
    public int Cancelled { get; set; }

    /// <summary>
    /// Number of requests skipped because their Hangfire job was already processing.
    /// </summary>
    public int SkippedProcessing { get; set; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
