namespace Lingarr.Server.Models.Api;

/// <summary>
/// Response model for batch-queueing translation jobs for multiple media items.
/// </summary>
public class TranslateMediaBatchResponse
{
    /// <summary>
    /// Number of translations that were actually queued.
    /// </summary>
    public int TranslationsQueued { get; set; }

    /// <summary>
    /// Number of items requested for queueing.
    /// </summary>
    public int TotalRequested { get; set; }
}
