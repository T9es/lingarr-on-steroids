using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

public class VttWriter : ISubtitleWriter
{
    private IEnumerable<string> SubtitleItemToSubtitleEntry(SubtitleItem subtitleItem, int subtitleEntryNumber)
    {
        string FormatTimeCodeLine()
        {
            TimeSpan start = TimeSpan.FromMilliseconds(subtitleItem.StartTime);
            TimeSpan end = TimeSpan.FromMilliseconds(subtitleItem.EndTime);
            return $"{start:hh\\:mm\\:ss\\.fff} --> {end:hh\\:mm\\:ss\\.fff}";
        }

        List<string> lines = new List<string>();
        lines.Add(subtitleEntryNumber.ToString());
        lines.Add(FormatTimeCodeLine());
        var linesToUse = subtitleItem.TranslatedLines.Count > 0 ? subtitleItem.TranslatedLines : subtitleItem.Lines;
        lines.AddRange(linesToUse);

        return lines;
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
            for (int index = 0; index < items.Count; index++)
            {
                SubtitleItem subtitleItem = items[index];
                IEnumerable<string> lines = SubtitleItemToSubtitleEntry(subtitleItem, subtitleItem.Position);
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