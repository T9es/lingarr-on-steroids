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
                var isTranslatable = IsMeaningfullyTranslatable(providerText);

                return new NodeCandidate(
                    index,
                    subtitle,
                    structure,
                    providerText,
                    isTranslatable,
                    rawSourceChars);
            })
            .ToList();

        var deduplication = ProviderTextDeduper.Deduplicate(
            candidates
                .Where(candidate => candidate.IsTranslatable)
                .Select(candidate => new ProviderTextItem(candidate.Subtitle.Position, candidate.ProviderText))
                .ToList());

        var nodes = candidates
            .Select(candidate =>
            {
                if (!candidate.IsTranslatable)
                {
                    return candidate.ToNode(
                        SubtitleTranslationNodeKind.PassThrough,
                        representativePosition: null,
                        passThroughReason: "non-language");
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

    private static bool IsMeaningfullyTranslatable(string providerText)
    {
        var trimmed = providerText.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               !SubtitleFormatterService.IsMeaningless(trimmed) &&
               trimmed.Any(char.IsLetterOrDigit);
    }

    private sealed record NodeCandidate(
        int GlobalIndex,
        SubtitleItem Subtitle,
        SubtitleTextStructure Structure,
        string ProviderText,
        bool IsTranslatable,
        int RawSourceCharCount)
    {
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
                passThroughReason);
        }
    }
}
