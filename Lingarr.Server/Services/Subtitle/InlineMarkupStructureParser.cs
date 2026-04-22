using System.Text;
using System.Text.RegularExpressions;

namespace Lingarr.Server.Services.Subtitle;

internal sealed class InlineMarkupStructureParser
{
    private static readonly HashSet<string> BasicTagNames = ["i", "b", "u", "font", "ruby", "rt", "rb"];
    private static readonly Regex TimestampTagRegex = new(
        @"^(?:\d{2}:)?\d{2}:\d{2}\.\d{3}$",
        RegexOptions.Compiled);

    public List<SubtitleTextLine> Parse(IReadOnlyList<string> lines)
    {
        var parsedLines = new List<SubtitleTextLine>();
        for (var sourceLineIndex = 0; sourceLineIndex < lines.Count; sourceLineIndex++)
        {
            var parts = ParseLine(lines[sourceLineIndex]);
            parsedLines.Add(new SubtitleTextLine(sourceLineIndex, 0, parts, string.Empty));
        }

        return parsedLines;
    }

    private static List<SubtitleTextPart> ParseLine(string line)
    {
        var parts = new List<SubtitleTextPart>();
        var builder = new StringBuilder();

        void FlushText()
        {
            if (builder.Length == 0)
            {
                return;
            }

            var text = builder.ToString();
            builder.Clear();
            var isTranslatable = !string.IsNullOrWhiteSpace(text);
            parts.Add(new SubtitleTextPart(
                SubtitleTextPartKind.Text,
                text,
                isTranslatable,
                text));
        }

        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '<')
            {
                builder.Append(line[index]);
                continue;
            }

            var closing = line.IndexOf('>', index + 1);
            if (closing < 0)
            {
                builder.Append(line[index]);
                continue;
            }

            var tag = line[index..(closing + 1)];
            if (IsProtectedInlineTag(tag))
            {
                FlushText();
                parts.Add(new SubtitleTextPart(
                    SubtitleTextPartKind.InlineMarkupTag,
                    tag,
                    false,
                    string.Empty));
            }
            else
            {
                builder.Append(tag);
            }

            index = closing;
        }

        FlushText();
        return parts;
    }

    internal static bool IsProtectedInlineTag(string tag)
    {
        if (tag.Length < 3 || tag[0] != '<' || tag[^1] != '>')
        {
            return false;
        }

        var inner = tag[1..^1].Trim();
        if (string.IsNullOrEmpty(inner))
        {
            return false;
        }

        if (TimestampTagRegex.IsMatch(inner))
        {
            return true;
        }

        var isClosingTag = false;
        if (inner.StartsWith('/'))
        {
            isClosingTag = true;
            inner = inner[1..].Trim();
            if (string.IsNullOrEmpty(inner))
            {
                return false;
            }
        }

        if (isClosingTag)
        {
            return inner.Equals("c", StringComparison.OrdinalIgnoreCase) ||
                   inner.Equals("v", StringComparison.OrdinalIgnoreCase) ||
                   inner.Equals("lang", StringComparison.OrdinalIgnoreCase) ||
                   BasicTagNames.Contains(inner.ToLowerInvariant());
        }

        if (inner.StartsWith("c.", StringComparison.OrdinalIgnoreCase) ||
            inner.Equals("c", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (inner.Equals("v", StringComparison.OrdinalIgnoreCase) ||
            inner.StartsWith("v ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (inner.Equals("lang", StringComparison.OrdinalIgnoreCase) ||
            inner.StartsWith("lang ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = inner.Split(' ', 2, StringSplitOptions.TrimEntries)[0];
        if (!BasicTagNames.Contains(name.ToLowerInvariant()))
        {
            return false;
        }

        return name.Equals("font", StringComparison.OrdinalIgnoreCase) || inner.Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
