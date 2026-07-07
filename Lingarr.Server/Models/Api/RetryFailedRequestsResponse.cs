namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response payload for retrying all failed translation requests.
/// </summary>
public class RetryFailedRequestsResponse
{
    /// <summary>
    /// Total number of failed requests before retry execution.
    /// </summary>
    public int TotalFailed { get; set; }

    /// <summary>
    /// Number of failed requests that were moved back to pending.
    /// </summary>
    public int Retried { get; set; }

    /// <summary>
    /// Number of failed requests blocked because a matching active/pending request exists.
    /// </summary>
    public int BlockedByActiveRequest { get; set; }

    /// <summary>
    /// Number of failed requests remaining after retry execution.
    /// </summary>
    public int RemainingFailed { get; set; }

    /// <summary>
    /// Human-readable summary message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
