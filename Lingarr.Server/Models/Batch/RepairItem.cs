namespace Lingarr.Server.Models.Batch;

/// <summary>
/// Represents a failed subtitle item that needs to be repaired in a deferred repair batch.
/// </summary>
public class RepairItem
{
    /// <summary>
    /// The position of the subtitle in the original file (1-indexed)
    /// </summary>
    public int Position { get; set; }
    
    /// <summary>
    /// The original source text that failed to translate
    /// </summary>
    public string OriginalLine { get; set; } = string.Empty;
    
    /// <summary>
    /// The batch index this item originally came from (for logging/debugging)
    /// </summary>
    public int OriginalBatchIndex { get; set; }
}
