using System.ComponentModel.DataAnnotations.Schema;

namespace Lingarr.Core.Entities;

public class TranslationFailedCue : BaseEntity
{
    public int TranslationRequestId { get; set; }

    [ForeignKey(nameof(TranslationRequestId))]
    public TranslationRequest? TranslationRequest { get; set; }

    public int Position { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;
    public bool AutoApprovalEligible { get; set; }
    public DateTime? AutoApprovedAt { get; set; }
    public string? ApprovalSequenceHash { get; set; }
}
