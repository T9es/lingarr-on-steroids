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

    private static List<string> GetLinesToUse(
        SubtitleItem subtitleItem,
        bool stripSubtitleFormatting,
        out bool rendered)
    {
        var linesToUse = subtitleItem.TranslatedLines.Count > 0
            ? subtitleItem.TranslatedLines
            : subtitleItem.Lines;
        var joined = string.Join("\\N", linesToUse);
        // Only run plain-text rendering for ASS-sourced lines (or explicit stripping).
        // Valid SRT/VTT markup like <i>/<b>/<u> must survive source-file rewrites.
        rendered = stripSubtitleFormatting || PlainTextSubtitleOutputRenderer.ContainsAssMarkup(joined);
        return rendered
            ? PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(joined)
            : linesToUse.ToList();
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
                var linesToUse = GetLinesToUse(subtitleItem, stripSubtitleFormatting, out var rendered);
                var shouldSkip = rendered
                    ? PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(linesToUse)
                    : linesToUse.All(string.IsNullOrWhiteSpace);
                if (shouldSkip)
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
