using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOutputBoundaryTests
{
    [Theory]
    [InlineData(".srt", true)]
    [InlineData(".srt", false)]
    [InlineData(".vtt", true)]
    [InlineData(".vtt", false)]
    public async Task PlainTextWriters_RemoveAssMarkupAndDrawingOnlyCues(
        string outputFormat,
        bool stripSubtitleFormatting)
    {
        ISubtitleWriter writer = outputFormat == ".srt"
            ? new SrtWriter()
            : new VttWriter();
        using var stream = new MemoryStream();

        var items = new List<SubtitleItem>
        {
            new()
            {
                Position = 4,
                StartTime = 0,
                EndTime = 1000,
                Lines = [@"{\p1}m 0 0 l 10 10{\p0}"],
                PlaintextLines = [],
                TranslatedLines = [@"{\p1}m 0 0 l 10 10{\p0}"]
            },
            new()
            {
                Position = 9,
                StartTime = 1000,
                EndTime = 2000,
                Lines = [@"{\pos(10,20)}Hello"],
                PlaintextLines = ["Hello"],
                TranslatedLines = [@"{\t(0,500,\fs40)}Czesc"]
            }
        };

        await writer.WriteStreamAsync(
            stream,
            items,
            stripSubtitleFormatting);

        var output = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("Czesc", output, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\", output, StringComparison.Ordinal);
        Assert.DoesNotContain("m 0 0", output, StringComparison.Ordinal);
        Assert.DoesNotContain("10 10", output, StringComparison.Ordinal);
        Assert.Equal(4, items[0].Position);
        Assert.Equal(9, items[1].Position);

        if (outputFormat == ".srt")
        {
            var outputLines = output.Split(["\r\n", "\n"], StringSplitOptions.None);
            Assert.Equal("1", outputLines[0]);
        }
    }

    [Fact]
    public async Task SrtWriter_WhenDrawingCuesAreRemoved_KeepsSerializedNumbersSequential()
    {
        using var stream = new MemoryStream();
        var items = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                StartTime = 1000,
                EndTime = 2000,
                Lines = [@"{\p1}m 0 0 l 10 10{\p0}"]
            },
            new()
            {
                Position = 2,
                StartTime = 2000,
                EndTime = 3000,
                Lines = ["Dialogue two"]
            },
            new()
            {
                Position = 3,
                StartTime = 3000,
                EndTime = 4000,
                Lines = [@"{\p1}m 0 0 l 20 20{\p0}"]
            },
            new()
            {
                Position = 4,
                StartTime = 4000,
                EndTime = 5000,
                Lines = ["Dialogue four"]
            }
        };

        await new SrtWriter().WriteStreamAsync(stream, items, stripSubtitleFormatting: false);

        var output = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, blocks.Length);
        Assert.Equal("1", blocks[0].Split('\n')[0]);
        Assert.Contains("Dialogue two", blocks[0], StringComparison.Ordinal);
        Assert.Equal("2", blocks[1].Split('\n')[0]);
        Assert.Contains("Dialogue four", blocks[1], StringComparison.Ordinal);
    }
}
