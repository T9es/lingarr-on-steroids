namespace Lingarr.Server.Models;

public class SubtitleOutputBackfillResult
{
    public int BackfilledFiles { get; set; }
    public int BackfilledFromExternalSourceFiles { get; set; }
    public int BackfilledFromEmbeddedSourceFiles { get; set; }
    public int BackfillSkippedFiles { get; set; }
    public bool RequiresRetranslation { get; set; }
    public List<string> Errors { get; set; } = [];
}
