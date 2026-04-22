namespace Lingarr.Server.Models.Api;

public class SubtitleOutputReconciliationResponse
{
    public int MediaItemsScanned { get; set; }
    public int DeletedFiles { get; set; }
    public int BackfilledFiles { get; set; }
    public int BackfilledFromExternalSourceFiles { get; set; }
    public int BackfilledFromEmbeddedSourceFiles { get; set; }
    public int BackfillSkippedFiles { get; set; }
    public int QueuedTranslations { get; set; }
    public int QueuedForRetranslation { get; set; }
    public int SkippedUnsafeFiles { get; set; }
    public int SkippedActiveRequests { get; set; }
    public List<string> Errors { get; set; } = [];
}
