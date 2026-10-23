namespace Lingarr.Server.Models.Translation;

public sealed record MissingTranslationCue(
    int Position,
    string SourceText,
    bool AutoApprovalEligible,
    string? RejectedText = null,
    string? RejectionReason = null);
