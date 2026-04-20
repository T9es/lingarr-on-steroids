using System.Text;
using System.Text.RegularExpressions;

namespace Lingarr.Server.Services.Subtitle;

internal sealed class AssTextStructureParser
{
    private static readonly Regex DrawingModeRegex = new(@"\\p(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KaraokeRegex = new(@"\\k[fo]?\d+|\\K\d+|\\kt\d+", RegexOptions.Compiled);

    public List<SubtitleTextLine> Parse(IReadOnlyList<string> lines)
    {
        var parsedLines = new List<SubtitleTextLine>();
        for (var sourceLineIndex = 0; sourceLineIndex < lines.Count; sourceLineIndex++)
        {
            ParseLine(lines[sourceLineIndex], sourceLineIndex, parsedLines);
        }

        return parsedLines;
    }

    private static void ParseLine(string line, int sourceLineIndex, List<SubtitleTextLine> parsedLines)
    {
        var segmentParts = new List<SubtitleTextPart>();
        var segmentIndex = 0;
        var builder = new StringBuilder();
        var drawingMode = false;

        void FlushText()
        {
            if (builder.Length == 0)
            {
                return;
            }

            var text = builder.ToString();
            builder.Clear();
            if (drawingMode)
            {
                segmentParts.Add(new SubtitleTextPart(
                    SubtitleTextPartKind.AssDrawing,
                    text,
                    false,
                    string.Empty));
                return;
            }

            segmentParts.Add(new SubtitleTextPart(
                SubtitleTextPartKind.Text,
                text,
                true,
                text));
        }

        void FlushSegment(string breakAfter)
        {
            FlushText();
            parsedLines.Add(new SubtitleTextLine(
                sourceLineIndex,
                segmentIndex,
                segmentParts,
                breakAfter));
            segmentIndex++;
            segmentParts = [];
        }

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '{')
            {
                FlushText();
                var endIndex = line.IndexOf('}', index + 1);
                if (endIndex < 0)
                {
                    var remainder = line[index..];
                    segmentParts.Add(new SubtitleTextPart(
                        SubtitleTextPartKind.AssOverrideBlock,
                        remainder,
                        false,
                        string.Empty));
                    UpdateDrawingMode(remainder, ref drawingMode);
                    break;
                }

                var overrideBlock = line[index..(endIndex + 1)];
                var overrideKind = KaraokeRegex.IsMatch(overrideBlock)
                    ? SubtitleTextPartKind.AssKaraokeTag
                    : SubtitleTextPartKind.AssOverrideBlock;
                segmentParts.Add(new SubtitleTextPart(
                    overrideKind,
                    overrideBlock,
                    false,
                    string.Empty));
                UpdateDrawingMode(overrideBlock, ref drawingMode);
                index = endIndex;
                continue;
            }

            if (current == '\\' && index + 1 < line.Length)
            {
                var escaped = line[index + 1];
                if (escaped == 'N' || escaped == 'n')
                {
                    FlushSegment($"\\{escaped}");
                    index++;
                    continue;
                }

                if (escaped == 'h')
                {
                    FlushText();
                    segmentParts.Add(new SubtitleTextPart(
                        SubtitleTextPartKind.AssNonBreakingSpace,
                        "\\h",
                        false,
                        " "));
                    index++;
                    continue;
                }
            }

            builder.Append(current);
        }

        FlushSegment(string.Empty);
    }

    private static void UpdateDrawingMode(string overrideBlock, ref bool drawingMode)
    {
        var matches = DrawingModeRegex.Matches(overrideBlock);
        if (matches.Count == 0)
        {
            return;
        }

        var lastMatch = matches[^1];
        if (!int.TryParse(lastMatch.Groups[1].Value, out var mode))
        {
            return;
        }

        drawingMode = mode > 0;
    }
}
