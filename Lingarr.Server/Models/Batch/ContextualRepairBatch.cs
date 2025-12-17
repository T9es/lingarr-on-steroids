namespace Lingarr.Server.Models.Batch;

/// <summary>
/// Represents a contextual repair batch with failed items and their surrounding context.
/// Context ranges are merged for adjacent failures to avoid duplication.
/// </summary>
public class ContextualRepairBatch
{
    /// <summary>
    /// All items in the batch (both failed items needing translation and context-only items)
    /// </summary>
    public List<BatchSubtitleItem> Items { get; set; } = new();
    
    /// <summary>
    /// Positions of items that actually need translation (subset of Items)
    /// </summary>
    public HashSet<int> FailedPositions { get; set; } = new();
    
    /// <summary>
    /// The context ranges used to build this batch (for logging)
    /// </summary>
    public List<ContextRange> Ranges { get; set; } = new();
}

/// <summary>
/// Represents a contiguous range of subtitle positions
/// </summary>
public class ContextRange
{
    public int Start { get; set; }
    public int End { get; set; }
    
    public ContextRange(int start, int end)
    {
        Start = start;
        End = end;
    }
}
