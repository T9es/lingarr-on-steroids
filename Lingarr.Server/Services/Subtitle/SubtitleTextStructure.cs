using System.Globalization;
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

        if (ShouldRenderAsSingleVisibleText(translatableIndexes.Count))
        {
            return SubtitleTextStructure.RenderLocalMarkupAroundTranslatedText(Parts, translatedText);
        }

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

    private bool ShouldRenderAsSingleVisibleText(int translatablePartCount)
    {
        return translatablePartCount > 1 &&
            Parts.Any(part => part.Kind == SubtitleTextPartKind.AssOverrideBlock) &&
            !Parts.Any(part =>
                part.Kind == SubtitleTextPartKind.AssKaraokeTag ||
                part.Kind == SubtitleTextPartKind.InlineMarkupTag);
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

        var textElementBoundaries = GetTextElementBoundaries(normalized);
        var totalTextElements = textElementBoundaries.Count - 1;
        var weights = originalSegments
            .Select(segment => Math.Max(1, new StringInfo(segment).LengthInTextElements))
            .ToArray();
        var totalWeight = weights.Sum();
        var segments = new List<string>(originalSegments.Count);

        var previousElementBoundary = 0;
        var previousCharBoundary = 0;
        var cumulativeWeight = 0;

        for (var index = 0; index < originalSegments.Count - 1; index++)
        {
            cumulativeWeight += weights[index];
            var idealElementBoundary = (int)Math.Round((double)totalTextElements * cumulativeWeight / totalWeight);
            idealElementBoundary = Math.Clamp(
                idealElementBoundary,
                previousElementBoundary,
                totalTextElements);

            var boundaryElement = FindBoundary(
                normalized,
                textElementBoundaries,
                idealElementBoundary,
                previousElementBoundary,
                totalTextElements);
            var boundaryChar = textElementBoundaries[boundaryElement];
            segments.Add(normalized[previousCharBoundary..boundaryChar].Trim());
            previousElementBoundary = boundaryElement;
            previousCharBoundary = boundaryChar;
        }

        segments.Add(normalized[previousCharBoundary..].Trim());
        return segments;
    }

    private static int FindBoundary(
        string text,
        IReadOnlyList<int> textElementBoundaries,
        int idealBoundary,
        int minimumBoundary,
        int maximumBoundary)
    {
        if (idealBoundary <= minimumBoundary)
        {
            return minimumBoundary;
        }

        if (idealBoundary >= maximumBoundary)
        {
            return maximumBoundary;
        }

        for (var offset = 0; offset <= maximumBoundary; offset++)
        {
            var right = idealBoundary + offset;
            if (IsWhitespaceBoundary(text, textElementBoundaries, right, minimumBoundary, maximumBoundary))
            {
                return right;
            }

            var left = idealBoundary - offset;
            if (IsWhitespaceBoundary(text, textElementBoundaries, left, minimumBoundary, maximumBoundary))
            {
                return left;
            }
        }

        return idealBoundary;
    }

    private static bool IsWhitespaceBoundary(
        string text,
        IReadOnlyList<int> textElementBoundaries,
        int boundaryElement,
        int minimumBoundary,
        int maximumBoundary)
    {
        if (boundaryElement <= minimumBoundary || boundaryElement >= maximumBoundary)
        {
            return false;
        }

        var charBoundary = textElementBoundaries[boundaryElement];
        if (charBoundary <= 0 || charBoundary >= text.Length)
        {
            return false;
        }

        return char.IsWhiteSpace(text[charBoundary]) || char.IsWhiteSpace(text[charBoundary - 1]);
    }

    private static List<int> GetTextElementBoundaries(string text)
    {
        var boundaries = new List<int> { 0 };
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            boundaries.Add(enumerator.ElementIndex + enumerator.GetTextElement().Length);
        }

        if (boundaries[^1] != text.Length)
        {
            boundaries.Add(text.Length);
        }

        return boundaries;
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
        var parts = Lines
            .OrderBy(line => line.SourceLineIndex)
            .ThenBy(line => line.SegmentIndex)
            .SelectMany(line => line.Parts)
            .ToList();

        return [RenderLocalMarkupAroundTranslatedText(parts, translatedText)];
    }

    internal static string RenderLocalMarkupAroundTranslatedText(
        IReadOnlyList<SubtitleTextPart> parts,
        string translatedText)
    {
        var firstTextIndex = parts
            .Select((part, index) => (part, index))
            .Where(item => item.part.IsTranslatable)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (firstTextIndex < 0)
        {
            return string.Concat(parts.Select(part => part.SourceText));
        }

        var prefix = string.Concat(parts
            .Take(firstTextIndex)
            .Where(ShouldPreserveLocalPart)
            .Select(part => part.SourceText));
        var lastTextIndex = parts
            .Select((part, index) => (part, index))
            .Where(item => item.part.IsTranslatable)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Last();
        var preserveTrailingWrapper = ShouldPreserveTrailingWrapper(parts, firstTextIndex, lastTextIndex);
        var suffix = preserveTrailingWrapper
            ? string.Concat(parts
                .Skip(lastTextIndex + 1)
                .Where(ShouldPreserveLocalPart)
                .Select(part => part.SourceText))
            : string.Empty;
        var insertions = new List<TextInsertion>();
        for (var index = firstTextIndex + 1; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.IsTranslatable || !ShouldPreserveLocalPart(part))
            {
                continue;
            }

            if (preserveTrailingWrapper && index > lastTextIndex)
            {
                continue;
            }

            if (TryFindNearestTextMatch(translatedText, parts, index + 1, 1, out var nextStart, out _))
            {
                insertions.Add(new TextInsertion(nextStart, part.SourceText, insertions.Count));
                continue;
            }

            if (TryFindNearestTextMatch(translatedText, parts, index - 1, -1, out var previousStart, out var previousLength))
            {
                insertions.Add(new TextInsertion(previousStart + previousLength, part.SourceText, insertions.Count));
            }
        }

        return prefix + ApplyInsertions(translatedText, insertions) + suffix;
    }

    private static bool ShouldPreserveLocalPart(SubtitleTextPart part)
    {
        return part.Kind != SubtitleTextPartKind.AssNonBreakingSpace;
    }

    private static bool ShouldPreserveTrailingWrapper(
        IReadOnlyList<SubtitleTextPart> parts,
        int firstTextIndex,
        int lastTextIndex)
    {
        if (firstTextIndex <= 0 || lastTextIndex < firstTextIndex || lastTextIndex >= parts.Count - 1)
        {
            return false;
        }

        return !parts
            .Skip(firstTextIndex + 1)
            .Take(lastTextIndex - firstTextIndex - 1)
            .Any(ShouldPreserveLocalPart);
    }

    private static bool TryFindNearestTextMatch(
        string translatedText,
        IReadOnlyList<SubtitleTextPart> parts,
        int startIndex,
        int step,
        out int matchStart,
        out int matchLength)
    {
        for (var index = startIndex; index >= 0 && index < parts.Count; index += step)
        {
            var part = parts[index];
            if (!part.IsTranslatable || string.IsNullOrWhiteSpace(part.SourceText))
            {
                continue;
            }

            var sourceText = part.SourceText.Trim();
            if (!sourceText.Any(char.IsLetterOrDigit))
            {
                continue;
            }

            matchStart = translatedText.IndexOf(sourceText, StringComparison.Ordinal);
            if (matchStart < 0)
            {
                matchStart = translatedText.IndexOf(sourceText, StringComparison.OrdinalIgnoreCase);
            }

            if (matchStart >= 0)
            {
                matchLength = sourceText.Length;
                return true;
            }
        }

        matchStart = -1;
        matchLength = 0;
        return false;
    }

    private static string ApplyInsertions(string translatedText, List<TextInsertion> insertions)
    {
        if (insertions.Count == 0)
        {
            return translatedText;
        }

        var builder = new StringBuilder(translatedText);
        foreach (var insertion in insertions
                     .OrderByDescending(insertion => insertion.Position)
                     .ThenByDescending(insertion => insertion.Order))
        {
            builder.Insert(insertion.Position, insertion.Text);
        }

        return builder.ToString();
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

    private sealed record TextInsertion(int Position, string Text, int Order);
}
