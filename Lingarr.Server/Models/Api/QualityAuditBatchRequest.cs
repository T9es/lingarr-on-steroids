namespace Lingarr.Server.Models.Api;

public class RequeueAllQualityAuditFindingsRequest
{
    /// <summary>
    /// Optional filter: only requeue findings with these issue types.
    /// If null or empty, all non-queued, non-dismissed findings are requeued.
    /// </summary>
    public List<string>? IssueTypes { get; set; }
}

public class DismissAllQualityAuditFindingsRequest
{
    /// <summary>
    /// Optional filter: only dismiss findings with these issue types.
    /// If null or empty, all non-queued, non-dismissed findings are dismissed.
    /// </summary>
    public List<string>? IssueTypes { get; set; }
}

public class TranslationCompareEditRequest
{
    public List<TranslationCompareEdit> Edits { get; set; } = [];
}

public class TranslationCompareEdit
{
    public int Position { get; set; }
    public string TranslatedText { get; set; } = string.Empty;
}
