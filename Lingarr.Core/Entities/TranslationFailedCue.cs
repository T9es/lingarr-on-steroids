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

    /// <summary>
    /// The provider translation that was rejected for this position, kept so a user can
    /// inspect and explicitly accept it without weakening provider validation.
    /// </summary>
    public string? RejectedText { get; set; }

    /// <summary>
    /// Stable rejection reason for <see cref="RejectedText"/>, for example source-echo or
    /// wrong-target-language.
    /// </summary>
    public string? RejectionReason { get; set; }
}
