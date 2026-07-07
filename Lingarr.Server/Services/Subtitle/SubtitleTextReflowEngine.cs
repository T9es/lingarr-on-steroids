using System.Globalization;

namespace Lingarr.Server.Services.Subtitle;

internal static class SubtitleTextReflowEngine
{
    public static List<string> Reflow(
        IReadOnlyList<string> translatedLines,
        IReadOnlyList<string> sourceVisibleLines)
    {
        var lineCount = sourceVisibleLines.Count;
        if (lineCount == 0)
        {
            return [];
        }

        var normalized = string.Join(
            ' ',
            translatedLines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line)));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Enumerable.Repeat(string.Empty, lineCount).ToList();
        }

        if (lineCount == 1)
        {
            return [normalized];
        }

        var elements = SplitTextElements(normalized);
        if (elements.Count == 0)
        {
            return Enumerable.Repeat(string.Empty, lineCount).ToList();
        }

        if (elements.Count < lineCount)
        {
            return [normalized, .. Enumerable.Repeat(string.Empty, lineCount - 1)];
        }

        var sourceWidths = sourceVisibleLines
            .Select(line => Math.Max(1.0, MeasureDisplayWidth(line.Trim())))
            .ToArray();
        var totalSourceWidth = sourceWidths.Sum();
        var prefixWidths = BuildPrefixWidths(elements);
        var totalWidth = prefixWidths[^1];
        var totalWordCount = CountWords(normalized);

        var costs = new double[lineCount + 1, elements.Count + 1];
        var previous = new int[lineCount + 1, elements.Count + 1];

        for (var line = 0; line <= lineCount; line++)
        {
            for (var index = 0; index <= elements.Count; index++)
            {
                costs[line, index] = double.PositiveInfinity;
                previous[line, index] = -1;
            }
        }

        costs[0, 0] = 0;
        for (var line = 1; line <= lineCount; line++)
        {
            var minEnd = line;
            var maxEnd = elements.Count - (lineCount - line);
            for (var end = minEnd; end <= maxEnd; end++)
            {
                for (var start = line - 1; start < end; start++)
                {
                    if (double.IsPositiveInfinity(costs[line - 1, start]))
                    {
                        continue;
                    }

                    var segmentCost = ScoreSegment(
                        normalized,
                        elements,
                        prefixWidths,
                        start,
                        end,
                        sourceWidths[line - 1],
                        totalSourceWidth,
                        totalWidth,
                        totalWordCount);
                    var totalCost = costs[line - 1, start] + segmentCost;
                    if (totalCost >= costs[line, end])
                    {
                        continue;
                    }

                    costs[line, end] = totalCost;
                    previous[line, end] = start;
                }
            }
        }

        if (previous[lineCount, elements.Count] < 0)
        {
            return GreedyReflow(elements, lineCount);
        }

        return BuildResult(normalized, elements, previous, lineCount);
    }

    private static double ScoreSegment(
        string text,
        IReadOnlyList<TextElement> elements,
        IReadOnlyList<double> prefixWidths,
        int start,
        int end,
        double sourceWidth,
        double totalSourceWidth,
        double totalWidth,
        int totalWordCount)
    {
        var segmentWidth = prefixWidths[end] - prefixWidths[start];
        var targetWidth = Math.Max(1.0, totalWidth * sourceWidth / totalSourceWidth);
        var balance = Math.Pow((segmentWidth - targetWidth) / targetWidth, 2) * 8;

        var segment = Slice(text, elements, start, end);
        var trimmed = segment.Trim();
        if (trimmed.Length == 0)
        {
            return 10_000;
        }

        var orphanPenalty = totalWordCount > 2 && CountWords(trimmed) == 1 ? 2.5 : 0;
        var boundaryPenalty = end < elements.Count
            ? ScoreBoundary(elements[end - 1], elements[end])
            : 0;

        return balance + orphanPenalty + boundaryPenalty;
    }

    private static double ScoreBoundary(TextElement left, TextElement right)
    {
        if (left.IsWhitespace)
        {
            return 0;
        }

        if (right.IsWhitespace)
        {
            return 0.15;
        }

        if (left.IsCjk && right.IsCjk)
        {
            return 1.0;
        }

        if (left.IsTokenContinuation && right.IsTokenContinuation)
        {
            return 12.0;
        }

        if (IsSentencePunctuation(left))
        {
            return 0.05;
        }

        if (IsSoftPunctuation(left))
        {
            return 0.35;
        }

        if (left.IsWordLike && right.IsWordLike)
        {
            return 7.5;
        }

        return 3.0;
    }

    private static List<string> BuildResult(
        string text,
        IReadOnlyList<TextElement> elements,
        int[,] previous,
        int lineCount)
    {
        var boundaries = new int[lineCount + 1];
        boundaries[lineCount] = elements.Count;

        for (var line = lineCount; line > 0; line--)
        {
            boundaries[line - 1] = previous[line, boundaries[line]];
        }

        var result = new List<string>(lineCount);
        for (var line = 0; line < lineCount; line++)
        {
            result.Add(Slice(text, elements, boundaries[line], boundaries[line + 1]).Trim());
        }

        return result;
    }

    private static List<string> GreedyReflow(IReadOnlyList<TextElement> elements, int lineCount)
    {
        var result = new List<string>(lineCount);
        var previous = 0;
        for (var line = 0; line < lineCount - 1; line++)
        {
            var remainingLines = lineCount - line - 1;
            var boundary = Math.Clamp(
                (int)Math.Round((double)elements.Count * (line + 1) / lineCount),
                previous + 1,
                elements.Count - remainingLines);
            result.Add(string.Concat(elements.Skip(previous).Take(boundary - previous).Select(element => element.Value)).Trim());
            previous = boundary;
        }

        result.Add(string.Concat(elements.Skip(previous).Select(element => element.Value)).Trim());
        return result;
    }

    private static string Slice(string text, IReadOnlyList<TextElement> elements, int start, int end)
    {
        var startIndex = elements[start].StartIndex;
        var endIndex = elements[end - 1].EndIndex;
        return text[startIndex..endIndex];
    }

    private static double[] BuildPrefixWidths(IReadOnlyList<TextElement> elements)
    {
        var prefix = new double[elements.Count + 1];
        for (var index = 0; index < elements.Count; index++)
        {
            prefix[index + 1] = prefix[index] + elements[index].Width;
        }

        return prefix;
    }

    private static double MeasureDisplayWidth(string text)
    {
        return SplitTextElements(text).Sum(element => element.Width);
    }

    private static int CountWords(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static List<TextElement> SplitTextElements(string text)
    {
        var elements = new List<TextElement>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var value = enumerator.GetTextElement();
            var startIndex = enumerator.ElementIndex;
            elements.Add(new TextElement(
                value,
                startIndex,
                startIndex + value.Length,
                MeasureTextElementWidth(value)));
        }

        return elements;
    }

    private static double MeasureTextElementWidth(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        if (ContainsEmojiOrWideSymbol(value))
        {
            return 2;
        }

        var width = 0.0;
        for (var index = 0; index < value.Length;)
        {
            var codePoint = char.ConvertToUtf32(value, index);
            var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            width += category switch
            {
                UnicodeCategory.NonSpacingMark => 0,
                UnicodeCategory.EnclosingMark => 0,
                UnicodeCategory.Format => 0,
                _ when IsWideCodePoint(codePoint) => 2,
                _ => 1
            };
            index += char.IsSurrogatePair(value, index) ? 2 : 1;
        }

        return Math.Max(0.5, width);
    }

    private static bool ContainsEmojiOrWideSymbol(string value)
    {
        for (var index = 0; index < value.Length;)
        {
            var codePoint = char.ConvertToUtf32(value, index);
            if (codePoint is >= 0x1F000 and <= 0x1FAFF)
            {
                return true;
            }

            index += char.IsSurrogatePair(value, index) ? 2 : 1;
        }

        return false;
    }

    private static bool IsWideCodePoint(int codePoint)
    {
        return codePoint is >= 0x1100 and <= 0x115F or
            >= 0x2E80 and <= 0xA4CF or
            >= 0xAC00 and <= 0xD7A3 or
            >= 0xF900 and <= 0xFAFF or
            >= 0xFE10 and <= 0xFE19 or
            >= 0xFE30 and <= 0xFE6F or
            >= 0xFF00 and <= 0xFF60 or
            >= 0xFFE0 and <= 0xFFE6;
    }

    private static bool IsSentencePunctuation(TextElement element)
    {
        return element.Value is "." or "!" or "?" or "。" or "！" or "？";
    }

    private static bool IsSoftPunctuation(TextElement element)
    {
        return element.Value is "," or ";" or ":" or "，" or "、" or "；" or "：";
    }

    private sealed record TextElement(
        string Value,
        int StartIndex,
        int EndIndex,
        double Width)
    {
        private UnicodeCategory Category => CharUnicodeInfo.GetUnicodeCategory(Value, 0);
        public bool IsWhitespace => Value.All(char.IsWhiteSpace);
        public bool IsCjk => Value.Length > 0 && IsWideCodePoint(char.ConvertToUtf32(Value, 0));
        public bool IsWordLike => char.IsLetterOrDigit(Value, 0) || Category == UnicodeCategory.ConnectorPunctuation;
        public bool IsTokenContinuation => Value.Length == 1 &&
            (char.IsLetterOrDigit(Value, 0) || UriTokenCharacters.Contains(Value[0]));
    }

    private static readonly HashSet<char> UriTokenCharacters = [
        '.', ':', '/', '?', '#', '[', ']', '@', '!', '$', '&', '\'', '(', ')',
        '*', '+', ',', ';', '=', '%', '-', '_', '~'
    ];
}
