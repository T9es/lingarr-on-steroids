using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public class VttWriter : ISubtitleWriter
{
    private IEnumerable<string> SubtitleItemToSubtitleEntry(
        SubtitleItem subtitleItem,
        IReadOnlyList<string> linesToUse)
    {
        string FormatTimeCodeLine()
        {
            TimeSpan start = TimeSpan.FromMilliseconds(subtitleItem.StartTime);
            TimeSpan end = TimeSpan.FromMilliseconds(subtitleItem.EndTime);
            return $"{start:hh\\:mm\\:ss\\.fff} --> {end:hh\\:mm\\:ss\\.fff}";
        }

        List<string> lines = new List<string>();
        lines.Add(subtitleItem.Position.ToString());
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

            await writer.WriteLineAsync("WEBVTT");
            await writer.WriteLineAsync();

            List<SubtitleItem> items = subtitleItems.ToList();
            foreach (var subtitleItem in items)
            {
                var linesToUse = GetLinesToUse(subtitleItem);
                if (PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(linesToUse))
                {
                    continue;
                }

                IEnumerable<string> lines = SubtitleItemToSubtitleEntry(subtitleItem, linesToUse);
                foreach (string line in lines)
                {
                    await writer.WriteLineAsync(line);
                }

                await writer.WriteLineAsync();
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
