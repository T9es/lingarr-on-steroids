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

        // First: Strip entire ASS drawing blocks: {\p1}...{\p0}
        // Drawing mode is enabled by {\p1} (or {\p2}, etc.) and disabled by {\p0}
        // Everything between these tags is vector drawing data that should not be translated
        string cleaned = Regex.Replace(input, @"\{\\p[1-9]\d*\}.*?\{\\p0\}", string.Empty, RegexOptions.Singleline);

        // Remove remaining SSA/ASS style tags: {\...}
        // Use Singleline mode to handle tags that span multiple lines
        cleaned = Regex.Replace(cleaned, @"\{.*?\}", string.Empty, RegexOptions.Singleline);

        // Remove HTML-style tags: <...>
        // Use Singleline mode to handle tags that span multiple lines
        cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty, RegexOptions.Singleline);

        // Replace SSA line breaks with spaces
        cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ");

        // Replace tab characters (escaped or literal)
        cleaned = cleaned.Replace("\\t", " ").Replace("\t", " ");

        // Collapse multiple whitespace into a single space
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

        // Strip poison content (Music, Credits, Sound Effects)
        cleaned = StripPoisonContent(cleaned);

        return cleaned.Trim();
    }

    /// <summary>
    /// Strips specific "poison" content that often causes translation failures or is undesirable.
    /// Includes musical symbols, sound effects in brackets, and credit lines.
    /// </summary>
    private static string StripPoisonContent(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        // 1. Strip musical symbols
        // Symbols: ♪ ♫ ♬ ♭ ♮ ♯
        string stripped = Regex.Replace(input, @"[♪♫♬♭♮♯]", string.Empty);

        // 2. Strip sound effects in brackets or parentheses
        // e.g. [groans], (music plays), [laughter]
        // We use a non-greedy match.
        // NOTE: This might strip (parenthetical dialogue), but for translation stability it is safer.
        stripped = Regex.Replace(stripped, @"\[.*?\]|\(.*?\)", string.Empty);

        // 3. Strip URL-only lines (e.g. www.site.com)
        // If the remaining text is just a URL, clear it.
        // Simple heuristic: contains "www." or ".com" or "http" and has no spaces?
        // Actually, let's just look for lines that *start* with www/http
        if (Regex.IsMatch(stripped.Trim(), @"^(?:https?:\/\/|www\.)", RegexOptions.IgnoreCase))
        {
            return string.Empty;
        }

        // 4. Strip credit lines
        // e.g., "Captioning by...", "Synced by..."
        // Regex matches lines starting with these phrases (case insensitive)
        if (Regex.IsMatch(stripped.Trim(), @"^(?:captioning|synced|subtitle|translat|encoded).{0,10}by", RegexOptions.IgnoreCase))
        {
            return string.Empty;
        }

        return stripped;
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
            // If it was only tags, effectively empty
            return true;
        }

        // 2. Tokenize
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // 3. Count "Drawing" tokens vs "Text" tokens
        // Drawing tokens:
        // - Single letters commonly used in ASS drawings: m, n, l, b, s, p, c (case-insensitive)
        // - Valid numbers
        
        var drawingTokensCount = 0;
        var totalTokens = tokens.Length;

        // Valid single-letter drawing commands (case-insensitive)
        // m: move, n: move (no close), l: line, b: cubic bezier, s: spline, p: extend spline, c: close spline
        var drawingCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "m", "n", "l", "b", "s", "p", "c" 
        };

        foreach (var token in tokens)
        {
            bool isDrawingToken = false;

            // Check if it's a command letter
            if (drawingCommands.Contains(token))
            {
                isDrawingToken = true;
            }
            // Check if it's a number
            else if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                isDrawingToken = true;
            }
            // Check for specific garbage tokens often seen in corrupted drawings (optional heuristic)
            // e.g. "0y", "0o", but strictly speaking these are "unknown".
            // We will count them as NON-drawing tokens for safety, relying on the ratio to handle them.

            if (isDrawingToken)
            {
                drawingTokensCount++;
            }
        }

        // 4. Density Ratio Check
        // If > 80% of the tokens are drawing commands/numbers, assume it is a drawing.
        // We require at least a few tokens to avoid false positives on short text like "Room 101" (2 tokens, 50-100% match depending on "Room")
        
        double ratio = (double)drawingTokensCount / totalTokens;

        if (totalTokens > 2)
        {
            // Heuristic: If > 80% of tokens are drawing-like, it's a drawing.
            // Example: "m 0 0 ... 0y" (50 tokens, 49 valid) -> 98% -> TRUE.
            // Example: "m 100 people live here" (6 tokens, 2 valid: m, 100) -> 33% -> FALSE.
            if (ratio > 0.8)
            {
                return true;
            }
        }
        else
        {
            // Fallback for short lines (1-2 tokens)
            // Be stricter to avoid false positives.
            // "100" -> number (don't delete, could be "100" years)
            // "m 100" -> valid drawing start, OR "m 100" (typo for "my 100"?) -> ambiguous.
            // "l" -> single letter "l" (garbage? or "I" typo?) -> existing logic.
            
            // Existing single-letter garbage check logic
            if (totalTokens == 1)
            {
                var token = tokens[0];
                if (token.Length == 1)
                {
                    // "I", "a", "A" might be valid words. "m", "l", "p", etc. are likely garbage/drawings.
                    // Ideally we only keep "I" and numbers.
                    if (char.IsDigit(token[0])) return false; // Keep "1"
                    if (string.Equals(token, "I", StringComparison.OrdinalIgnoreCase)) return false; // Keep "I" or "i"? Usually "I".
                    if (string.Equals(token, "a", StringComparison.OrdinalIgnoreCase)) return false; // Keep "a" or "A"

                    // If it's a single letter and not a common word/number, treat as garbage
                    return true;
                }
            }
            
            // For 2 tokens, e.g. "m 100", checks need to be careful.
            // Let's rely on ratio > 0.99 (basically 100%) for small lines, meaning ALL tokens must be valid.
            if (ratio >= 0.99)
            {
                // But wait, "m 100" is a valid drawing.
                // "10 20" is likely not a drawing (just numbers).
                // A valid drawing usually has at least one COMMAND letter.
                bool hasCommand = tokens.Any(t => drawingCommands.Contains(t));
                if (hasCommand) return true;
            }
        }

        return false;
    }
}