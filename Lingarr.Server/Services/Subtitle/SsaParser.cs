using System.Text;
using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public class SsaParser : ISubtitleParser
{
    private const string SCRIPT_INFO_SECTION = "[Script Info]";
    private const string V4_PLUS_STYLES_SECTION = "[V4+ Styles]";
    private const string V4_STYLES_SECTION = "[V4 Styles]";
    private const string EVENTS_SECTION = "[Events]";
    private const string FORMAT_PREFIX = "Format:";
    private const string DIALOGUE_PREFIX = "Dialogue:";
    private const string WRAP_STYLE_PREFIX = "WrapStyle:";

    public List<SubtitleItem> ParseStream(Stream ssaStream, Encoding encoding)
    {
        if (!ssaStream.CanRead || !ssaStream.CanSeek)
        {
            throw new ArgumentException("Subtitle must be seekable and readable");
        }

        // seek the beginning of the stream
        ssaStream.Position = 0;
        using var reader = new StreamReader(ssaStream, encoding, true);

        var items = new List<SubtitleItem>();
        var currentSection = string.Empty;
        var ssaFormat = new SsaFormat();
        Dictionary<string, int>? columnIndexes = null;

        var positionCounter = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmedLine = line.Trim();

            // Handle section changes
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal))
            {
                currentSection = NormalizeSectionName(trimmedLine);
                switch (currentSection)
                {
                    case SCRIPT_INFO_SECTION:
                        ssaFormat.ScriptInfo.Add(SCRIPT_INFO_SECTION);
                        break;
                    case V4_PLUS_STYLES_SECTION:
                        ssaFormat.Styles.Add(V4_PLUS_STYLES_SECTION);
                        break;
                    case V4_STYLES_SECTION:
                        ssaFormat.Styles.Add(V4_STYLES_SECTION);
                        break;
                    case EVENTS_SECTION:
                        ssaFormat.EventsFormat.Add(EVENTS_SECTION);
                        break;
                }

                continue;
            }

            // Store original section content
            switch (currentSection)
            {
                case SCRIPT_INFO_SECTION:
                    ssaFormat.ScriptInfo.Add(trimmedLine);
                    if (trimmedLine.StartsWith(WRAP_STYLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        var wrapStyleValue = trimmedLine.Substring(WRAP_STYLE_PREFIX.Length).Trim();
                        if (int.TryParse(wrapStyleValue, out int wrapStyleInt))
                        {
                            ssaFormat.WrapStyle = (SsaWrapStyle)wrapStyleInt;
                        }
                    }

                    break;
                case V4_PLUS_STYLES_SECTION:
                    ssaFormat.Styles.Add(trimmedLine);
                    break;
                case V4_STYLES_SECTION:
                    ssaFormat.Styles.Add(trimmedLine);
                    break;
                case EVENTS_SECTION:
                    if (trimmedLine.StartsWith(FORMAT_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        ssaFormat.EventsFormat.Add(trimmedLine);
                        var columns = trimmedLine.Substring(FORMAT_PREFIX.Length).Split(',')
                            .Select(c => c.Trim())
                            .ToList();
                        columnIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (var index = 0; index < columns.Count; index++)
                        {
                            columnIndexes[columns[index]] = index;
                        }
                    }
                    else if (trimmedLine.StartsWith(DIALOGUE_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        columnIndexes ??= CreateDefaultColumnIndexes();
                        var dialogue = ParseDialogueLine(trimmedLine, columnIndexes, ssaFormat);
                        if (dialogue != null)
                        {
                            dialogue.Position = positionCounter++;
                            dialogue.SsaFormat = ssaFormat;
                            items.Add(dialogue);
                        }
                    }

                    break;
            }
        }

        return items;
    }

    private static string NormalizeSectionName(string line)
    {
        if (line.Equals(SCRIPT_INFO_SECTION, StringComparison.OrdinalIgnoreCase))
        {
            return SCRIPT_INFO_SECTION;
        }

        if (line.Equals(V4_PLUS_STYLES_SECTION, StringComparison.OrdinalIgnoreCase))
        {
            return V4_PLUS_STYLES_SECTION;
        }

        if (line.Equals(V4_STYLES_SECTION, StringComparison.OrdinalIgnoreCase))
        {
            return V4_STYLES_SECTION;
        }

        if (line.Equals(EVENTS_SECTION, StringComparison.OrdinalIgnoreCase))
        {
            return EVENTS_SECTION;
        }

        return line;
    }

    private static Dictionary<string, int> CreateDefaultColumnIndexes()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Layer"] = 0,
            ["Marked"] = 0,
            ["Start"] = 1,
            ["End"] = 2,
            ["Style"] = 3,
            ["Name"] = 4,
            ["MarginL"] = 5,
            ["MarginR"] = 6,
            ["MarginV"] = 7,
            ["Effect"] = 8,
            ["Text"] = 9
        };
    }

    private static List<string> SplitTextByWrapStyle(string text, SsaWrapStyle wrapStyle)
    {
        return wrapStyle switch
        {
            // Smart wrapping modes only recognize \N as line breaks
            SsaWrapStyle.Smart or SsaWrapStyle.SmartWideLowerLine => 
                text.Split(["\\N"], StringSplitOptions.None).ToList(),
            
            // End-of-line wrapping only recognizes \N as line breaks
            SsaWrapStyle.EndOfLine => 
                text.Split(["\\N"], StringSplitOptions.None).ToList(),
            
            // No wrapping mode recognizes both \N and \n as line breaks
            SsaWrapStyle.None => 
                Regex.Split(text, @"\\N|\\n").ToList(),
            
            // Default case for any undefined wrap styles
            _ => [text]
        };
    }

    private SubtitleItem? ParseDialogueLine(string line, Dictionary<string, int> columnIndexes, SsaFormat ssaFormat)
    {
        if (!TryGetRequiredColumnIndexes(
                columnIndexes,
                out var textIndex,
                out var startIndex,
                out var endIndex,
                out var styleIndex,
                out var marginLIndex,
                out var marginRIndex,
                out var marginVIndex,
                out var effectIndex))
        {
            return null;
        }

        // Find the first 9 commas (corresponding to the format fields before Text)
        var textFieldStart = -1;
        var commaCount = 0;
        for (var index = DIALOGUE_PREFIX.Length; index < line.Length; index++)
        {
            if (line[index] != ',')
            {
                continue;
            }

            commaCount++;
            if (commaCount != textIndex)
            {
                continue;
            }

            textFieldStart = index + 1;
            break;
        }

        if (textFieldStart == -1 || textFieldStart >= line.Length)
        {
            return null;
        }

        // Extract the parts before the Text field
        var dialoguePrefix = line.Substring(DIALOGUE_PREFIX.Length, textFieldStart - DIALOGUE_PREFIX.Length - 1);
        var dialogueParts = dialoguePrefix.Split(',');

        if (dialogueParts.Length < textIndex)
        {
            return null;
        }

        // Extract basic timing information
        var startTime = ParseSsaTimecode(dialogueParts[startIndex].Trim());
        var endTime = ParseSsaTimecode(dialogueParts[endIndex].Trim());

        // Extract the text part directly without splitting
        var text = line.Substring(textFieldStart).Trim();

        if (startTime < 0 || endTime < 0 || string.IsNullOrEmpty(text))
        {
            return null;
        }

        var textLines = SplitTextByWrapStyle(text, ssaFormat.WrapStyle);
        var plaintextLines = textLines.Select(SubtitleFormatterService.RemoveMarkup).ToList();

        // Create SsaDialogue info
        var ssaDialogue = new SsaDialogue
        {
            Marked = dialogueParts[0].Trim(),
            Style = dialogueParts[styleIndex].Trim(),
            MarginL = dialogueParts[marginLIndex].Trim(),
            MarginR = dialogueParts[marginRIndex].Trim(),
            MarginV = dialogueParts[marginVIndex].Trim(),
            Effect = dialogueParts[effectIndex].Trim()
        };
        

        if (columnIndexes.TryGetValue("Name", out var nameIndex) && nameIndex < dialogueParts.Length)
        {
            ssaDialogue.Name = dialogueParts[nameIndex].Trim();
        }

        return new SubtitleItem
        {
            StartTime = startTime,
            EndTime = endTime,
            Lines = textLines,
            PlaintextLines = plaintextLines,
            SsaDialogue = ssaDialogue,
            SsaFormat = ssaFormat
        };
    }

    private static bool TryGetRequiredColumnIndexes(
        Dictionary<string, int> columnIndexes,
        out int textIndex,
        out int startIndex,
        out int endIndex,
        out int styleIndex,
        out int marginLIndex,
        out int marginRIndex,
        out int marginVIndex,
        out int effectIndex)
    {
        textIndex = -1;
        startIndex = -1;
        endIndex = -1;
        styleIndex = -1;
        marginLIndex = -1;
        marginRIndex = -1;
        marginVIndex = -1;
        effectIndex = -1;

        var hasRequiredColumns =
            columnIndexes.TryGetValue("Text", out textIndex) &&
            columnIndexes.TryGetValue("Start", out startIndex) &&
            columnIndexes.TryGetValue("End", out endIndex) &&
            columnIndexes.TryGetValue("Style", out styleIndex) &&
            columnIndexes.TryGetValue("MarginL", out marginLIndex) &&
            columnIndexes.TryGetValue("MarginR", out marginRIndex) &&
            columnIndexes.TryGetValue("MarginV", out marginVIndex) &&
            columnIndexes.TryGetValue("Effect", out effectIndex);

        return hasRequiredColumns;
    }

    private static int ParseSsaTimecode(string timestamp)
    {
        if (TimeSpan.TryParse(timestamp, out var timeSpan))
        {
            return (int)timeSpan.TotalMilliseconds;
        }

        return -1;
    }
}
