namespace Lingarr.Server.Models.Api;

/// <summary>
/// A cue whose provider translation was rejected by validation, including the rejected text so
/// the user can inspect it and explicitly accept it through the compare edit flow.
/// </summary>
public class FailedCueDetailDto
{
    public int Position { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string? RejectedText { get; set; }
    public string? RejectionReason { get; set; }
}
