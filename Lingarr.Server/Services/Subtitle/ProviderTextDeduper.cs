namespace Lingarr.Server.Services.Subtitle;

internal sealed record ProviderTextItem(
    int Position,
    string ProviderText,
    SubtitleSemanticKind SemanticKind = SubtitleSemanticKind.Dialogue);

internal sealed class ProviderTextDeduplicationResult
{
    public required List<ProviderTextItem> Representatives { get; init; }
    public required IReadOnlyDictionary<int, int> RepresentativePositionByPosition { get; init; }
    public required IReadOnlyDictionary<int, List<int>> MemberPositionsByRepresentativePosition { get; init; }

    public int RepresentativeCount => Representatives.Count;
    public int DuplicatePositionCount => RepresentativePositionByPosition.Count - RepresentativeCount;

    public bool IsRepresentative(int position)
    {
        return RepresentativePositionByPosition.TryGetValue(position, out var representativePosition) &&
               representativePosition == position;
    }

    public int GetRepresentativePosition(int position)
    {
        return RepresentativePositionByPosition[position];
    }

    public IReadOnlyList<int> GetMemberPositions(int representativePosition)
    {
        return MemberPositionsByRepresentativePosition.TryGetValue(representativePosition, out var members)
            ? members
            : [];
    }
}

internal static class ProviderTextDeduper
{
    public static ProviderTextDeduplicationResult Deduplicate(IReadOnlyList<ProviderTextItem> items)
    {
        var representatives = new List<ProviderTextItem>(items.Count);
        var representativePositionByPosition = new Dictionary<int, int>(items.Count);
        var memberPositionsByRepresentativePosition = new Dictionary<int, List<int>>();
        var representativeByKey = new Dictionary<(SubtitleSemanticKind SemanticKind, string Text), ProviderTextItem>();

        foreach (var item in items)
        {
            var key = (item.SemanticKind, Normalize(item.ProviderText));
            if (!representativeByKey.TryGetValue(key, out var representative))
            {
                representative = item;
                representativeByKey[key] = representative;
                representatives.Add(representative);
                memberPositionsByRepresentativePosition[representative.Position] = [representative.Position];
            }
            else
            {
                memberPositionsByRepresentativePosition[representative.Position].Add(item.Position);
            }

            representativePositionByPosition[item.Position] = representative.Position;
        }

        return new ProviderTextDeduplicationResult
        {
            Representatives = representatives,
            RepresentativePositionByPosition = representativePositionByPosition,
            MemberPositionsByRepresentativePosition = memberPositionsByRepresentativePosition
        };
    }

    public static string Normalize(string providerText)
    {
        return SubtitleTextStructure.NormalizeProviderTranslationText(providerText).Trim();
    }

    /// <summary>
    /// Builds the set of normalized provider texts that occur at least
    /// <see cref="RepeatedChantThreshold"/> times across a file. Repeated identical
    /// text reads as a chant or refrain, which the provider may legitimately omit.
    /// </summary>
    public static IReadOnlySet<string> BuildRepeatedTexts(IEnumerable<string> providerTexts)
    {
        return providerTexts
            .Select(Normalize)
            .Where(text => text.Length > 0)
            .GroupBy(text => text, StringComparer.Ordinal)
            .Where(group => group.Count() >= RepeatedChantThreshold)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private const int RepeatedChantThreshold = 3;
}
