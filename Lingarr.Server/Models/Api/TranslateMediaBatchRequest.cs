using Lingarr.Core.Enum;

namespace Lingarr.Server.Models.Api;

/// <summary>
/// Request model for batch-queueing translation jobs for multiple media items.
/// </summary>
public class TranslateMediaBatchRequest
{
    /// <summary>
    /// The media items to queue translations for.
    /// </summary>
    public List<TranslateMediaBatchItem> Items { get; set; } = new();
}

/// <summary>
/// A single media item in a batch queue request.
/// </summary>
public class TranslateMediaBatchItem
{
    /// <summary>
    /// The ID of the media item.
    /// </summary>
    public int MediaId { get; set; }

    /// <summary>
    /// The type of media (Movie or Episode).
    /// </summary>
    public MediaType MediaType { get; set; }
}
