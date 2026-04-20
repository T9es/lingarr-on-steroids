using System.IO;
using System.Text;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SsaParserTests
{
    [Fact]
    public void ParseStream_ShouldHandleIndentedCaseInsensitiveSections()
    {
        const string content = """
 [script info]
 Title: Example
 WrapStyle: 0

 [v4+ styles]
 Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
 Style: Default,Arial,28,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1

 [events]
 format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
 dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,Hello\NWorld
 """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var subtitles = new SsaParser().ParseStream(stream, Encoding.UTF8);

        var subtitle = Assert.Single(subtitles);
        Assert.Equal(["Hello", "World"], subtitle.Lines);
    }
}
