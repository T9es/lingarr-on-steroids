using System;
using System.IO;
using System.Linq;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleEntryCountingTests : IDisposable
{
    private readonly string _tempDirectory;

    public SubtitleEntryCountingTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void CountSubtitleEntries_ShouldCountDialogueEntriesForAssFiles()
    {
        var filePath = Path.Combine(_tempDirectory, "movie.eng.ass");
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("Title: Example");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("WrapStyle: 0");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine("Style: Default,Arial,28,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var index = 0; index < 60; index++)
        {
            builder.AppendLine($"Dialogue: 0,0:00:{index:D2}.00,0:00:{index + 1:D2}.00,Default,,0,0,0,,Line {index + 1}");
        }

        File.WriteAllText(filePath, builder.ToString());

        var entryCount = SubtitleExtractionService.CountSubtitleEntries(filePath);

        Assert.Equal(60, entryCount);
        Assert.False(SubtitleExtractionService.IsSparseSubtitle(filePath));
    }

    [Fact]
    public void CountSubtitleEntries_ShouldNotCountTimingOnlySrtBlocksAsDialogueEntries()
    {
        var filePath = Path.Combine(_tempDirectory, "timing-only.en.srt");
        var content = string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(1, 60).Select(index =>
                $"{index}{Environment.NewLine}00:00:{index % 60:00},000 --> 00:00:{index % 60:00},500"));

        File.WriteAllText(filePath, content);

        var entryCount = SubtitleExtractionService.CountSubtitleEntries(filePath);

        Assert.Equal(0, entryCount);
        Assert.True(SubtitleExtractionService.IsSparseSubtitle(filePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
