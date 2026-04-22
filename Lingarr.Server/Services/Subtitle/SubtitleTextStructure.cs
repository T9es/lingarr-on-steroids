using System.Text;

namespace Lingarr.Server.Services.Subtitle;

internal enum SubtitleStructureMode
{
    PlainText,
    Ass,
    InlineMarkup
}

internal enum SubtitleTextPartKind
{
    Text,
    InlineMarkupTag,
    AssOverrideBlock,
    AssKaraokeTag,
    AssDrawing,
    AssNonBreakingSpace
}

internal sealed record SubtitleTextPart(
    SubtitleTextPartKind Kind,
    string SourceText,
    bool IsTranslatable,
    string ProviderVisibleText);

internal sealed class SubtitleTextLine
{
    public SubtitleTextLine(
        int sourceLineIndex,
        int segmentIndex,
        List<SubtitleTextPart> parts,
        string breakAfter)
    {
        SourceLineIndex = sourceLineIndex;
        SegmentIndex = segmentIndex;
        Parts = parts;
        BreakAfter = breakAfter;
    }

    public int SourceLineIndex { get; }
    public int SegmentIndex { get; }
    public List<SubtitleTextPart> Parts { get; }
    public string BreakAfter { get; }

    public string ProviderVisibleText => string.Concat(Parts.Select(part => part.ProviderVisibleText));
    public bool HasVisibleText => !string.IsNullOrWhiteSpace(ProviderVisibleText);

    public string Render(string translatedText)
    {
        var translatableIndexes = Parts
            .Select((part, index) => (part, index))
            .Where(item => item.part.IsTranslatable)
            .Select(item => item.index)
            .ToList();

        if (translatableIndexes.Count == 0)
        {
            return string.Concat(Parts.Select(part => part.SourceText));
        }

        var originalTranslatableTexts = translatableIndexes
            .Select(index => Parts[index].SourceText)
            .ToList();
        var translatedSegments = DistributeTranslation(translatedText, originalTranslatableTexts);

        var translatedIndex = 0;
        var builder = new StringBuilder();
        for (var index = 0; index < Parts.Count; index++)
        {
            var part = Parts[index];
            if (!part.IsTranslatable)
            {
                builder.Append(part.SourceText);
                continue;
            }

            builder.Append(translatedSegments[translatedIndex]);
            translatedIndex++;
        }

        return builder.ToString();
    }

    private static List<string> DistributeTranslation(string translatedText, IReadOnlyList<string> originalSegments)
    {
        if (originalSegments.Count == 0)
        {
            return [];
        }

        if (originalSegments.Count == 1)
        {
            return [translatedText];
        }

        var normalized = translatedText.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return Enumerable.Repeat(string.Empty, originalSegments.Count).ToList();
        }

        var weights = originalSegments
            .Select(segment => Math.Max(1, segment.Length))
            .ToArray();
        var totalWeight = weights.Sum();
        var segments = new List<string>(originalSegments.Count);

        var previousBoundary = 0;
        var cumulativeWeight = 0;

        for (var index = 0; index < originalSegments.Count - 1; index++)
        {
            cumulativeWeight += weights[index];
            var idealBoundary = (int)Math.Round((double)normalized.Length * cumulativeWeight / totalWeight);
            idealBoundary = Math.Clamp(
                idealBoundary,
                previousBoundary + 1,
                normalized.Length - (originalSegments.Count - index - 1));

            var boundary = FindBoundary(normalized, idealBoundary, previousBoundary + 1);
            segments.Add(normalized[previousBoundary..boundary].Trim());
            previousBoundary = boundary;
        }

        segments.Add(normalized[previousBoundary..].Trim());
        return segments;
    }

    private static int FindBoundary(string text, int idealBoundary, int minimumBoundary)
    {
        if (idealBoundary <= minimumBoundary)
        {
            return minimumBoundary;
        }

        for (var offset = 0; offset < text.Length; offset++)
        {
            var right = idealBoundary + offset;
            if (right >= minimumBoundary && right < text.Length && char.IsWhiteSpace(text[right]))
            {
                return right + 1;
            }

            var left = idealBoundary - offset;
            if (left >= minimumBoundary && left < text.Length && char.IsWhiteSpace(text[left]))
            {
                return left + 1;
            }
        }

        return idealBoundary;
    }
}

internal sealed class SubtitleTextStructure
{
    private readonly List<SubtitleTextLine> _providerVisibleLines;
    private readonly Dictionary<int, List<SubtitleTextLine>> _segmentsBySourceLineIndex;

    public SubtitleTextStructure(
        SubtitleStructureMode mode,
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<SubtitleTextLine> lines)
    {
        Mode = mode;
        SourceLines = sourceLines.ToList();
        Lines = lines
            .OrderBy(line => line.SourceLineIndex)
            .ThenBy(line => line.SegmentIndex)
            .ToList();
        _providerVisibleLines = Lines
            .Where(line => line.HasVisibleText)
            .ToList();
        _segmentsBySourceLineIndex = Lines
            .GroupBy(line => line.SourceLineIndex)
            .ToDictionary(group => group.Key, group => group.OrderBy(line => line.SegmentIndex).ToList());
    }

    public SubtitleStructureMode Mode { get; }
    public IReadOnlyList<string> SourceLines { get; }
    public IReadOnlyList<SubtitleTextLine> Lines { get; }
    public int VisibleLineCount => _providerVisibleLines.Count;

    public string ProviderVisibleText => string.Join(
        '\n',
        _providerVisibleLines.Select(line => line.ProviderVisibleText.Trim()));

    public int ProviderVisibleCharCount => ProviderVisibleText.Length;

    public bool IsProviderTranslationCompatible(string translatedProviderText)
    {
        if (VisibleLineCount <= 1)
        {
            return true;
        }

        var translatedLines = SplitProviderTranslationLines(translatedProviderText);
        return translatedLines.Count == VisibleLineCount;
    }

    public List<string> ApplyProviderTranslation(string translatedProviderText)
    {
        if (VisibleLineCount == 0)
        {
            return SourceLines.ToList();
        }

        var translatedLines = SplitProviderTranslationLines(translatedProviderText);
        var translationByLine = BuildLineAssignments(translatedProviderText, translatedLines);
        var translatedBySegment = _providerVisibleLines
            .Select((line, index) => new
            {
                Segment = line,
                Translated = translationByLine[index]
            })
            .ToDictionary(
                item => (item.Segment.SourceLineIndex, item.Segment.SegmentIndex),
                item => item.Translated);

        var output = new List<string>(SourceLines.Count);
        for (var sourceLineIndex = 0; sourceLineIndex < SourceLines.Count; sourceLineIndex++)
        {
            if (!_segmentsBySourceLineIndex.TryGetValue(sourceLineIndex, out var segments))
            {
                output.Add(SourceLines[sourceLineIndex]);
                continue;
            }

            var builder = new StringBuilder();
            foreach (var segment in segments)
            {
                if (!translatedBySegment.TryGetValue((segment.SourceLineIndex, segment.SegmentIndex), out var translatedSegment))
                {
                    translatedSegment = segment.ProviderVisibleText;
                }

                builder.Append(segment.Render(translatedSegment));
                builder.Append(segment.BreakAfter);
            }

            output.Add(builder.ToString());
        }

        return output;
    }

    public List<string> ApplyProviderTranslationAsSingleVisibleText(string translatedProviderText)
    {
        if (VisibleLineCount == 0)
        {
            return SourceLines.ToList();
        }

        var translatedText = NormalizeProviderTranslationText(translatedProviderText)
            .Replace("\\N", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\N", StringComparison.Ordinal);
        var insertedTranslation = false;
        var builder = new StringBuilder();

        foreach (var sourceLineIndex in Enumerable.Range(0, SourceLines.Count))
        {
            if (!_segmentsBySourceLineIndex.TryGetValue(sourceLineIndex, out var segments))
            {
                builder.Append(SourceLines[sourceLineIndex]);
                continue;
            }

            foreach (var segment in segments)
            {
                foreach (var part in segment.Parts)
                {
                    if (part.IsTranslatable)
                    {
                        if (!insertedTranslation)
                        {
                            builder.Append(translatedText);
                            insertedTranslation = true;
                        }

                        continue;
                    }

                    if (part.Kind == SubtitleTextPartKind.AssNonBreakingSpace)
                    {
                        continue;
                    }

                    builder.Append(part.SourceText);
                }
            }
        }

        return insertedTranslation ? [builder.ToString()] : SourceLines.ToList();
    }

    private List<string> BuildLineAssignments(string translatedProviderText, IReadOnlyList<string> translatedLines)
    {
        if (VisibleLineCount == 1)
        {
            return [NormalizeProviderTranslationText(translatedProviderText)];
        }

        if (translatedLines.Count == VisibleLineCount)
        {
            return translatedLines.ToList();
        }

        return SubtitleTextReflowEngine.Reflow(
            translatedLines,
            _providerVisibleLines.Select(line => line.ProviderVisibleText).ToList());
    }

    internal static List<string> SplitProviderTranslationLines(string translatedProviderText)
    {
        var normalized = NormalizeProviderTranslationText(translatedProviderText)
            .Replace("\\N", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None).ToList();
        while (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == 0 ? [string.Empty] : lines;
    }

    internal static string NormalizeProviderTranslationText(string translatedProviderText)
    {
        return (translatedProviderText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}
