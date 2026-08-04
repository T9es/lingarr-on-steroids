using System.Text.RegularExpressions;
using Lingarr.Server.Extensions;

namespace Lingarr.Server.Services.Subtitle;

internal static class PlainTextSubtitleOutputRenderer
{
    private static readonly Regex AssDrawingBlockRegex = new(
        @"\{\\p[1-9]\d*\}.*?\{\\p0\}",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AssOverrideBlockRegex = new(
        @"\{[^}]*\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]*>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(
        @"\s{2,}",
        RegexOptions.Compiled);

    private static readonly HashSet<string> DrawingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "m", "n", "l", "b", "s", "p", "c"
    };

    public static List<string> ConvertToPlainTextLines(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return [];
        }

        var normalized = SubtitleFormatterService.NormalizeLineBreaks(translatedText)
            .Replace("\\n", "\\N", StringComparison.Ordinal);
        var segments = normalized.Split("\\N", StringSplitOptions.None);
        var lines = new List<string>();

        foreach (var segment in segments)
        {
            var plainText = RemoveMarkupForOutput(segment);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                continue;
            }

            lines.AddRange(plainText.SplitIntoLines(42));
        }

        return lines;
    }

    public static bool ContainsAssMarkup(string text)
    {
        return AssDrawingBlockRegex.IsMatch(text) || AssOverrideBlockRegex.IsMatch(text);
    }

    public static bool ShouldSkipSubtitle(IReadOnlyList<string> plainTextLines)
    {
        return plainTextLines.Count == 0 || plainTextLines.All(ShouldSkipLine);
    }

    private static bool ShouldSkipLine(string line)
    {
        var text = line.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (IsStructuralPunctuationCue(text))
        {
            return false;
        }

        if (LooksLikeDrawingTokenSequence(text))
        {
            return true;
        }

        if (LooksLikeSingleTokenVisualDebris(text))
        {
            return true;
        }

        if (HasWordLikeToken(text))
        {
            return false;
        }

        return SubtitleFormatterService.IsAssDrawingCommand(text);
    }

    private static string RemoveMarkupForOutput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cleaned = AssDrawingBlockRegex.Replace(input, string.Empty);
        cleaned = AssOverrideBlockRegex.Replace(cleaned, string.Empty);
        cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);

        cleaned = cleaned
            .Replace("\\N", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\h", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        cleaned = WhitespaceRegex.Replace(cleaned, " ");

        return cleaned.Trim();
    }

    private static bool IsStructuralPunctuationCue(string text)
    {
        return text.Length > 0 && text.All(character => !char.IsLetterOrDigit(character));
    }

    private static bool HasWordLikeToken(string text)
    {
        return GetTokens(text).Any(token =>
        {
            var letterCount = token.Count(char.IsLetter);
            return letterCount >= 2 ||
                   string.Equals(token, "I", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "a", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool LooksLikeDrawingTokenSequence(string text)
    {
        var tokens = GetTokens(text).ToList();
        if (tokens.Count == 0)
        {
            return false;
        }

        var drawingTokensCount = tokens.Count(IsDrawingToken);
        var ratio = (double)drawingTokensCount / tokens.Count;

        if (tokens.Count == 1)
        {
            return DrawingCommands.Contains(tokens[0]);
        }

        if (tokens.Count == 2)
        {
            return ratio >= 0.99 && tokens.Any(token => DrawingCommands.Contains(token));
        }

        return ratio > 0.8;
    }

    private static bool LooksLikeSingleTokenVisualDebris(string text)
    {
        var tokens = GetTokens(text).ToList();
        if (tokens.Count != 1)
        {
            return false;
        }

        var token = tokens[0];
        if (token.Length >= 4 && token.All(char.IsDigit))
        {
            return true;
        }

        return token.Length >= 5 &&
               token.All(IsAsciiLetter) &&
               token.All(character =>
                   char.ToUpperInvariant(character) == char.ToUpperInvariant(token[0]));
    }

    private static bool IsDrawingToken(string token)
    {
        return DrawingCommands.Contains(token) ||
               double.TryParse(
                   token,
                   System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out _);
    }

    private static IEnumerable<string> GetTokens(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
