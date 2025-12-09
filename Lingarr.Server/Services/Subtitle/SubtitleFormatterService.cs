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
        string cleaned = Regex.Replace(input, @"\{.*?\}", string.Empty);

        // Remove HTML-style tags: <...>
        cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty);

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
            return false;
        }
        
        // First strip any HTML/font tags to get the raw content
        string cleaned = RemoveMarkup(input);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }
        
        // ASS drawing commands pattern:
        // - Must start with 'm' (move command) followed by coordinates
        // - Can contain drawing commands: m (move), b (bezier), l (line), n (move no-close), 
        //   c (close), s (b-spline), p (extend spline)
        // - Contains only numbers, decimals, spaces, minus signs, and single letter commands
        // Pattern matches: "m 625.52 110.81 b 622.39 111.8 ..."
        // Also matches without 'm' prefix: "625.52 110.8 b 622.4 111.8 ..."
        var drawingPattern = @"^(m\s+)?[\d.-]+\s+[\d.-]+(\s+[bclmnps]?\s*[\d.\s-]+)*$";
        return Regex.IsMatch(cleaned.Trim(), drawingPattern, RegexOptions.IgnoreCase);
    }
}