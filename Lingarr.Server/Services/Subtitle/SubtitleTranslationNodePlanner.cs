using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

internal enum SubtitleTranslationNodeKind
{
    PassThrough,
    Representative,
    DuplicateMember
}

internal sealed record SubtitleTranslationNode(
    int GlobalIndex,
    SubtitleItem Subtitle,
    SubtitleTextStructure Structure,
    string ProviderText,
    bool IsTranslatable,
    int RawSourceCharCount,
    SubtitleTranslationNodeKind Kind,
    int? RepresentativePosition,
    SubtitleSemanticKind SemanticKind,
    bool CanPreserveSourceWhenProviderMissing,
    string? PassThroughReason);

internal sealed class SubtitleTranslationPlan
{
    public required IReadOnlyList<SubtitleTranslationNode> Nodes { get; init; }
    public required IReadOnlyList<SubtitleTranslationNode> RepresentativeNodes { get; init; }
    public required ProviderTextDeduplicationResult Deduplication { get; init; }
}

internal static class SubtitleTranslationNodePlanner
{
    public static SubtitleTranslationPlan Plan(
        IReadOnlyList<SubtitleItem> subtitles,
        bool stripSubtitleFormatting,
        bool preserveAssFormatting)
    {
        var candidates = subtitles
            .Select((subtitle, index) =>
            {
                var structure = SubtitleTextStructureFactory.Create(
                    subtitle,
                    stripSubtitleFormatting,
                    preserveAssFormatting);
                var providerText = structure.ProviderVisibleText;
                var rawSourceLines = SubtitleTextStructureFactory.GetSourceLines(
                    subtitle,
                    stripSubtitleFormatting,
                    preserveAssFormatting);
                var rawSourceChars = string.Join(" ", rawSourceLines).Length;
                var semanticClassification = SubtitleSemanticClassifier.Classify(
                    subtitle,
                    providerText,
                    subtitle.SsaDialogue?.Style);

                return new NodeCandidate(
                    index,
                    subtitle,
                    structure,
                    providerText,
                    semanticClassification,
                    rawSourceChars);
            })
            .ToList();

        // Repeated identical text across a file is a chant or refrain: the provider may
        // legitimately omit it, so such cues are preserved from source instead of failing.
        // The set is computed from pass-one classifications (which do not affect
        // translatability) and applied on the second pass.
        var repeatedProviderTexts = ProviderTextDeduper.BuildRepeatedTexts(
            candidates
                .Where(candidate => candidate.IsTranslatable)
                .Select(candidate => candidate.ProviderText));

        var deduplication = ProviderTextDeduper.Deduplicate(
            candidates
                .Where(candidate => candidate.IsTranslatable)
                .Select(candidate => new ProviderTextItem(candidate.Subtitle.Position, candidate.ProviderText))
                .ToList());

        var nodes = candidates
            .Select(candidate =>
            {
                candidate = candidate with
                {
                    SemanticClassification = SubtitleSemanticClassifier.Classify(
                        candidate.Subtitle,
                        candidate.ProviderText,
                        candidate.Subtitle.SsaDialogue?.Style,
                        repeatedProviderTexts)
                };

                if (!candidate.IsTranslatable)
                {
                    return candidate.ToNode(
                        SubtitleTranslationNodeKind.PassThrough,
                        representativePosition: null,
                        passThroughReason: candidate.SemanticClassification.Reason);
                }

                var representativePosition = deduplication.GetRepresentativePosition(candidate.Subtitle.Position);
                var kind = representativePosition == candidate.Subtitle.Position
                    ? SubtitleTranslationNodeKind.Representative
                    : SubtitleTranslationNodeKind.DuplicateMember;

                return candidate.ToNode(kind, representativePosition, passThroughReason: null);
            })
            .ToList();

        return new SubtitleTranslationPlan
        {
            Nodes = nodes,
            RepresentativeNodes = nodes
                .Where(node => node.Kind == SubtitleTranslationNodeKind.Representative)
                .ToList(),
            Deduplication = deduplication
        };
    }

    private sealed record NodeCandidate(
        int GlobalIndex,
        SubtitleItem Subtitle,
        SubtitleTextStructure Structure,
        string ProviderText,
        SubtitleSemanticClassification SemanticClassification,
        int RawSourceCharCount)
    {
        public bool IsTranslatable => SemanticClassification.ShouldRequestProvider;

        public SubtitleTranslationNode ToNode(
            SubtitleTranslationNodeKind kind,
            int? representativePosition,
            string? passThroughReason)
        {
            return new SubtitleTranslationNode(
                GlobalIndex,
                Subtitle,
                Structure,
                ProviderText,
                IsTranslatable,
                RawSourceCharCount,
                kind,
                representativePosition,
                SemanticClassification.Kind,
                SemanticClassification.CanPreserveSourceWhenProviderMissing,
                passThroughReason);
        }
    }
}
