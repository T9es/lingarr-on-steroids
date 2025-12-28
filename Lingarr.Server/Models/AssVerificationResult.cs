namespace Lingarr.Server.Models;

/// <summary>
/// Result of ASS drawing verification scan.
/// </summary>
public class AssVerificationResult
{
    public int TotalFilesScanned { get; set; }
    public int FilesWithDrawings { get; set; }
    public List<AssVerificationItem> FlaggedItems { get; set; } = new();
}

/// <summary>
/// Individual item flagged during ASS verification.
/// </summary>
public class AssVerificationItem
{
    public int MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string MediaTitle { get; set; } = string.Empty;
    public string SubtitlePath { get; set; } = string.Empty;
    public int SuspiciousLineCount { get; set; }
    public List<string> SuspiciousLines { get; set; } = new();
    public bool Dismissed { get; set; }
    
    /// <summary>
    /// Indicates whether this media item is already queued for translation (Pending or InProgress).
    /// </summary>
    public bool IsQueued { get; set; }
}
