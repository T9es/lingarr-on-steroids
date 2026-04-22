namespace Lingarr.Server.Models.Api;

public class SubtitleOutputReconciliationResponse
{
    public int MediaItemsScanned { get; set; }
    public int DeletedFiles { get; set; }
    public int QueuedTranslations { get; set; }
    public int SkippedUnsafeFiles { get; set; }
    public int SkippedActiveRequests { get; set; }
    public List<string> Errors { get; set; } = [];
}
