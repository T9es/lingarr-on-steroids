namespace Lingarr.Server.Models;

/// <summary>
/// Result for a single translation request alignment check.
/// </summary>
public class SubtitleAlignmentCheckResult
{
    public int RequestId { get; set; }
    public string? Title { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public bool ShiftDetected { get; set; }
    public int ShiftStartPosition { get; set; }
    public int ShiftMagnitude { get; set; }
    public int ConsecutiveMismatches { get; set; }
    public double Confidence { get; set; }
    public string? SourcePath { get; set; }
    public string? TranslatedPath { get; set; }
    public List<string> Samples { get; set; } = [];
}

/// <summary>
/// Summary of an alignment scan across multiple translation requests.
/// </summary>
public class SubtitleAlignmentCheckSummary
{
    public int TotalScanned { get; set; }
    public int ShiftsDetected { get; set; }
    public List<SubtitleAlignmentCheckResult> Results { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
