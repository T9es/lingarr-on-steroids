using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Forked from: https://github.com/AlexPoint/SubtitlesParser
/// Writer for the .srt subtitle files
/// </summary>
public class SrtWriter : ISubtitleWriter
{
    /// <summary>
    /// Converts a subtitle item into the lines for an SRT subtitle entry
    /// </summary>
    /// <param name="subtitleItem">The SubtitleItem to convert</param>
    /// <param name="subtitleEntryNumber">The subtitle number for the entry (increments sequentially from 1)</param>
    /// <param name="linesToUse">The rendered lines to write for the subtitle entry</param>
    /// <returns>A list of strings to write as an SRT subtitle entry</returns>
    private IEnumerable<string> SubtitleItemToSubtitleEntry(
        SubtitleItem subtitleItem,
        int subtitleEntryNumber,
        IReadOnlyList<string> linesToUse)
    {
        // take the start and end timestamps and format it as a timecode line
        string FormatTimeCodeLine()
        {
            TimeSpan start = TimeSpan.FromMilliseconds(subtitleItem.StartTime);
            TimeSpan end = TimeSpan.FromMilliseconds(subtitleItem.EndTime);
            return $"{start:hh\\:mm\\:ss\\,fff} --> {end:hh\\:mm\\:ss\\,fff}";
        }

        List<string> lines = new List<string>();
        lines.Add(subtitleEntryNumber.ToString());
        lines.Add(FormatTimeCodeLine());
        lines.AddRange(linesToUse);

        return lines;
    }

    private static List<string> GetLinesToUse(SubtitleItem subtitleItem)
    {
        var linesToUse = subtitleItem.TranslatedLines.Count > 0
            ? subtitleItem.TranslatedLines
            : subtitleItem.Lines;
        return PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(string.Join("\\N", linesToUse));
    }
    
    /// <inheritdoc />
    public async Task WriteStreamAsync(
        Stream stream, 
        IEnumerable<SubtitleItem> subtitleItems, 
        bool stripSubtitleFormatting)
    {
        try
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream), "Stream cannot be null");
            }

            if (!stream.CanWrite)
            {
                throw new InvalidOperationException("Stream is not writable.");
            }

            await using TextWriter writer = new StreamWriter(stream);

            List<SubtitleItem>
                items = subtitleItems.ToList(); // avoid multiple enumeration since we're using a for instead of foreach
            var subtitleEntryNumber = 0;
            foreach (var subtitleItem in items)
            {
                var linesToUse = GetLinesToUse(subtitleItem);
                if (PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(linesToUse))
                {
                    continue;
                }

                subtitleEntryNumber++;
                // Create a subtitle entry
                IEnumerable<string>
                    lines = SubtitleItemToSubtitleEntry(subtitleItem,
                        subtitleEntryNumber,
                        linesToUse); // add one because subtitle entry numbers start at 1 instead of 0
                foreach (string line in lines)
                {
                    await writer.WriteLineAsync(line);
                }

                await writer.WriteLineAsync(); // empty line between subtitle entries
            }

            await writer.FlushAsync();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Error writing to stream: {ex.Message}");
            throw;
        }
    }
}
