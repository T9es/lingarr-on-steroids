using System.Text;
using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public class VttParser : ISubtitleParser
{
    private readonly string[] _timeDelimiters = { "-->", "- >", "->" };
    
    private static readonly Regex RegexTimeCodes = new(
        @"^-?\d+:-?\d+:-?\d+\.-?\d+\s*-->\s*-?\d+:-?\d+:-?\d+\.-?\d+", 
        RegexOptions.Compiled);
    private static readonly Regex RegexTimeCodesMiddle = new(
        @"^-?\d+:-?\d+\.-?\d+\s*-->\s*-?\d+:-?\d+:-?\d+\.-?\d+", 
        RegexOptions.Compiled);
    private static readonly Regex RegexTimeCodesShort = new(
        @"^-?\d+:-?\d+\.-?\d+\s*-->\s*-?\d+:-?\d+\.-?\d+", 
        RegexOptions.Compiled);
    
    private const int MinimumSubtitleBlockSize = 2;
    private int _positionCounter = 0;

    public List<SubtitleItem> ParseStream(Stream subtitleStream, Encoding encoding)
    {
        try
        {
            ValidateStream(subtitleStream);
            using var reader = new StreamReader(subtitleStream, encoding, true);
            _positionCounter = 0;
            var subtitles = ParseSubtitles(reader).ToList();
            
            if (subtitles.Count == 0)
            {
                throw new FormatException("No valid subtitles found in the WebVTT stream");
            }

            return subtitles;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing WebVTT file: {ex.Message}");
            return new List<SubtitleItem>();
        }
    }

    private IEnumerable<SubtitleItem> ParseSubtitles(TextReader reader)
    {
        var currentBlock = new List<string>();
        string? line;

        while ((line = ReadNonEmptyLine(reader)) != null)
        {
            if (line.StartsWith("WEBVTT", StringComparison.Ordinal))
            {
                continue;
            }
            
            if (line == "STYLE" || line.StartsWith("STYLE ", StringComparison.Ordinal))
            {
                SkipBlock(reader);
                continue;
            }
            
            if (line == "REGION" || line.StartsWith("REGION ", StringComparison.Ordinal))
            {
                SkipBlock(reader);
                continue;
            }
            
            if (line == "NOTE" || line.StartsWith("NOTE ", StringComparison.Ordinal))
            {
                SkipBlock(reader);
                continue;
            }
            
            if (line.StartsWith("X-TIMESTAMP-MAP=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            currentBlock.Add(line);
            break;
        }

        while ((line = ReadNonEmptyLine(reader)) != null)
        {
            if (line == "NOTE" || line.StartsWith("NOTE ", StringComparison.Ordinal))
            {
                SkipBlock(reader);
                continue;
            }
            
            if (IsBlockSeparator(line, currentBlock))
            {
                if (TryParseBlock(currentBlock, out var subtitle))
                {
                    yield return subtitle;
                }
                currentBlock.Clear();
            }
            currentBlock.Add(line);
        }

        if (TryParseBlock(currentBlock, out var lastSubtitle))
        {
            yield return lastSubtitle;
        }
    }

    private static void SkipBlock(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                break;
            }
        }
    }

    private static string? ReadNonEmptyLine(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }
        return null;
    }

    private bool IsBlockSeparator(string line, List<string> currentBlock)
    {
        if (currentBlock.Count == 0)
        {
            return false;
        }
        
        return IsTimeCodeLine(line);
    }

    private bool IsTimeCodeLine(string line)
    {
        return RegexTimeCodes.IsMatch(line) || 
               RegexTimeCodesMiddle.IsMatch(line) || 
               RegexTimeCodesShort.IsMatch(line);
    }

    private bool TryParseBlock(List<string> block, out SubtitleItem subtitle)
    {
        subtitle = new SubtitleItem();

        if (block.Count < MinimumSubtitleBlockSize)
        {
            return false;
        }

        int timeCodeIndex = -1;
        for (int i = 0; i < block.Count; i++)
        {
            if (IsTimeCodeLine(block[i]))
            {
                timeCodeIndex = i;
                break;
            }
        }

        if (timeCodeIndex == -1)
        {
            return false;
        }

        var timeCodeLine = block[timeCodeIndex];
        timeCodeLine = NormalizeTimeCodeLine(timeCodeLine);
        
        if (!TryParseTimeCodes(timeCodeLine, out var start, out var end))
        {
            return false;
        }

        if (timeCodeIndex > 0)
        {
            if (int.TryParse(block[0], out var pos))
            {
                subtitle.Position = pos;
            }
            else
            {
                subtitle.Position = ++_positionCounter;
            }
        }
        else
        {
            subtitle.Position = ++_positionCounter;
        }

        subtitle.StartTime = start;
        subtitle.EndTime = end;
        
        var textLines = block.Skip(timeCodeIndex + 1);
        ParseTextLines(textLines, subtitle);

        return subtitle.Lines.Count > 0;
    }

    private string NormalizeTimeCodeLine(string line)
    {
        var arrowIndex = line.IndexOf("-->", StringComparison.Ordinal);
        if (arrowIndex >= 0)
        {
            var afterArrow = line.Substring(arrowIndex + 3).Trim();
            var spaceIndex = afterArrow.IndexOf(' ');
            if (spaceIndex > 0)
            {
                var endTimePart = afterArrow.Substring(0, spaceIndex);
                var startTimePart = line.Substring(0, arrowIndex).Trim();
                line = $"{startTimePart} --> {endTimePart}";
            }
        }
        
        if (RegexTimeCodesShort.IsMatch(line))
        {
            line = "00:" + line.Replace("--> ", "--> 00:");
        }
        else if (RegexTimeCodesMiddle.IsMatch(line))
        {
            line = "00:" + line;
        }

        return line;
    }

    private bool TryParseTimeCodes(string line, out int start, out int end)
    {
        start = end = -1;
        var parts = line.Split(_timeDelimiters, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            return false;
        }

        start = ParseTimeCode(parts[0]);
        end = ParseTimeCode(parts[1]);

        if (start >= 0 && end >= 0 && end < start)
        {
            (start, end) = (end, start);
        }

        return start >= 0 && end >= 0;
    }

    private static int ParseTimeCode(string timeCode)
    {
        var parts = timeCode.Trim().Split(':', '.');
        
        if (parts.Length < 3 || parts.Length > 4)
        {
            return -1;
        }

        int hours, minutes, seconds, milliseconds;
        
        if (parts.Length == 4)
        {
            hours = int.Parse(parts[0]);
            minutes = int.Parse(parts[1]);
            seconds = int.Parse(parts[2]);
            milliseconds = int.Parse(parts[3].PadRight(3, '0').Substring(0, 3));
        }
        else
        {
            hours = 0;
            minutes = int.Parse(parts[0]);
            seconds = int.Parse(parts[1]);
            milliseconds = int.Parse(parts[2].PadRight(3, '0').Substring(0, 3));
        }

        if (minutes >= 60 || seconds >= 60)
        {
            return -1;
        }

        return hours * 3600000 + minutes * 60000 + seconds * 1000 + milliseconds;
    }

    private static void ParseTextLines(IEnumerable<string> lines, SubtitleItem subtitle)
    {
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            subtitle.Lines.Add(trimmedLine);
            subtitle.PlaintextLines.Add(
                SubtitleFormatterService.RemoveMarkup(trimmedLine)
            );
        }
    }

    private static void ValidateStream(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable");
        }

        stream.Position = 0;
    }
}