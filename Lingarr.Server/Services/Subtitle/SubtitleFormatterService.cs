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
        // to be filtered out.
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }
        
        // Single character check
        if (cleaned.Length == 1)
        {
            char c = cleaned[0];
            if (char.IsDigit(c))
            {
                // Keep numbers (e.g. "1")
                return false;
            }

            // STRICTER FILTER:
            // Even if it is a valid word ("I", "a", "O"), if it has heavy formatting commands, 
            // it is likely a sign or particle effect context.
            if (input.Contains(@"{\an") || input.Contains(@"\an") || input.Contains("face=") || input.Contains(@"\fn"))
            {
                return true; // Single letter with alignment/font tag -> Junk
            }
            
            // Standard valid 1-letter words allowed ONLY if no suspicious tags
            // We remove 'o'/'O' as they are too rare/poetic to risk keeping noise.
            // We keep 'I' (common pronoun).
            if (c == 'I') 
            {
                return false;
            }

            // Everything else -> Garbage
            return true;
        }
        
        // ASS drawing commands logic:
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
            return false;
        }

        return hasMoveCommand || numberCount >= 2;
    }
}