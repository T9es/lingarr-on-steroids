using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SrtWriterTests
{
    [Fact]
    public async Task WriteStreamAsync_PreservesValidSrtMarkupWhenFormattingIsNotStripped()
    {
        var writer = new SrtWriter();
        using var stream = new MemoryStream();
        await writer.WriteStreamAsync(
            stream,
            [
                new SubtitleItem
                {
                    Position = 1,
                    StartTime = 1000,
                    EndTime = 2000,
                    Lines = ["<i>italic line</i>"]
                }
            ],
            stripSubtitleFormatting: false);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("<i>italic line</i>", output);
    }

    [Fact]
    public async Task WriteStreamAsync_StripsAssMarkupFromAssSourcedLines()
    {
        var writer = new SrtWriter();
        using var stream = new MemoryStream();
        await writer.WriteStreamAsync(
            stream,
            [
                new SubtitleItem
                {
                    Position = 1,
                    StartTime = 1000,
                    EndTime = 2000,
                    Lines = ["{\\an8\\bord0}Hello"]
                }
            ],
            stripSubtitleFormatting: false);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("{", output);
        Assert.Contains("Hello", output);
    }

    [Fact]
    public async Task WriteStreamAsync_StripsSrtMarkupWhenExplicitlyRequested()
    {
        var writer = new SrtWriter();
        using var stream = new MemoryStream();
        await writer.WriteStreamAsync(
            stream,
            [
                new SubtitleItem
                {
                    Position = 1,
                    StartTime = 1000,
                    EndTime = 2000,
                    Lines = ["<i>italic line</i>"]
                }
            ],
            stripSubtitleFormatting: true);

        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("<i>", output);
        Assert.Contains("italic line", output);
    }
}
