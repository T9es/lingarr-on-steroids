namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response payload for retrying a single translation request.
/// </summary>
public class RetryTranslationRequestResponse
{
    /// <summary>
    /// ID of the request that was targeted for retry.
    /// </summary>
    public int RequestId { get; set; }

    /// <summary>
    /// Indicates whether the request was retried successfully.
    /// </summary>
    public bool Retried { get; set; }

    /// <summary>
    /// Indicates whether retry was blocked by an active/pending duplicate request.
    /// </summary>
    public bool BlockedByActiveRequest { get; set; }

    /// <summary>
    /// Human-readable summary message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
