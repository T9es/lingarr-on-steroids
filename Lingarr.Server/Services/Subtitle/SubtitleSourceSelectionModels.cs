using Lingarr.Core.Entities;

namespace Lingarr.Server.Services.Subtitle;

public enum SubtitleSourceCandidateRole
{
    PrimaryFullDialogue,
    PathologicalAssFallback,
    CaptionFallback,
    SupplementalForcedSigns,
    RejectedSparse,
    RejectedPathological,
    RejectedCorrupt,
    RejectedCommentary,
    RejectedLanguage,
    RejectedNonText
}

public sealed record SubtitleSourceCandidateAssessment(
    EmbeddedSubtitle Subtitle,
    SubtitleSourceCandidateRole Role,
    string? MatchedLanguage,
    int Score,
    int? EntryCount,
    string Reason);

public sealed class SubtitleSourceSelectionResult
{
    public EmbeddedSubtitle? SelectedSubtitle { get; init; }
    public string MatchedLanguage { get; init; } = string.Empty;
    public SubtitleSourceCandidateRole? SelectedRole { get; init; }
    public IReadOnlyList<SubtitleSourceCandidateAssessment> Assessments { get; init; } =
        Array.Empty<SubtitleSourceCandidateAssessment>();

    public IReadOnlyList<SubtitleSourceCandidateAssessment> SupplementalCandidates =>
        Assessments
            .Where(assessment =>
                assessment.Role == SubtitleSourceCandidateRole.SupplementalForcedSigns)
            .ToList();
}
