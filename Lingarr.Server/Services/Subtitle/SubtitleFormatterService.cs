using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleFormatterService : ISubtitleFormatterService
{
    /// <inheritdoc />
    public static string RemoveMarkup(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) {
            return string.Empty;
        }

        // Remove SSA/ASS style tags: {\...}
        // Use Singleline mode to handle tags that span multiple lines
        string cleaned = Regex.Replace(input, @"\{.*?\}", string.Empty, RegexOptions.Singleline);

        // Remove HTML-style tags: <...>
        // Use Singleline mode to handle tags that span multiple lines
        cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty, RegexOptions.Singleline);

        // Replace SSA line breaks with spaces
        cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ");

        // Replace tab characters (escaped or literal)
        cleaned = cleaned.Replace("\\t", " ").Replace("\t", " ");

        // Collapse multiple whitespace into a single space
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

        return cleaned.Trim();
    }
    
    /// <summary>
    /// Detects if a subtitle line contains only ASS drawing commands (vector graphics).
    /// These are paths used for rendering shapes/signs and are not translatable text.
    /// </summary>
    /// <param name="input">The subtitle line to check.</param>
    /// <returns>True if the line appears to be an ASS drawing command, false otherwise.</returns>
    public static bool IsAssDrawingCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            // If the line is empty/whitespace, we consider it "garbage" or a "drawing command" 
            // in the context of filtering, so we return true to have it removed.
            return true;
        }
        
        // First strip any HTML/font tags to get the raw content
        string cleaned = RemoveMarkup(input);
        
        // If the line consists ONLY of tags (cleaned is empty), treat it as a "drawing" or non-text line
        // to be filtered out. Ideally this should be a separate check, but for the purpose of
        // "stripping garbage", this effectively removes empty lines caused by tag-stripping.
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }
        
        // ASS drawing commands logic:
        // Instead of a complex regex that can fail or backtrack, we tokenize the string.
        // Valid tokens in a drawing command are:
        // - Single letters: m, n, l, b, s, p, c (case insensitive)
        // - Numbers (integer or decimal, positive or negative)
        // 
        // We reject if we find anything else (like actual words).
        //
        // SAFEGUARD: To avoid false positives on lines that happen to be just a number (e.g. "1997" or "10"),
        // we require either:
        // 1. A Move command "m" was present.
        // 2. OR at least 2 numbers were present (a coordinate pair).
        
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool hasMoveCommand = false;
        int numberCount = 0;
        
        foreach (var token in tokens)
        {
            // Check if token is a valid command letter
            if (token.Length == 1 && "mnlbspc".Contains(char.ToLowerInvariant(token[0])))
            {
                if (char.ToLowerInvariant(token[0]) == 'm') hasMoveCommand = true;
                continue;
            }
            
            // Check if token is a number
            if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                numberCount++;
                continue;
            }
            
            // If it's neither a known command nor a number, it's likely text
            // e.g. "To", "You", "Someday" -> these are not drawing commands
            return false;
        }

        // A valid drawing command sequence requires sufficient evidence it is not just text.
        // If we saw an explicit "m" command, it's definitely a drawing (as long as all tokens were valid).
        // If we didn't see "m", we might be seeing a coordinate fragment (e.g. "100 200 ...").
        // To be safe against single numbers (e.g. "1997"), we require at least 2 numbers.
        return hasMoveCommand || numberCount >= 2;
    }
}