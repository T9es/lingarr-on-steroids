using System.Linq;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOutputModeHelperTests
{
    [Theory]
    [InlineData(".ass", SubtitleOutputMode.MatchSource, new[] { ".ass" })]
    [InlineData(".ass", SubtitleOutputMode.AssOnly, new[] { ".ass" })]
    [InlineData(".ass", SubtitleOutputMode.SrtOnly, new[] { ".srt" })]
    [InlineData(".ass", SubtitleOutputMode.Both, new[] { ".ass", ".srt" })]
    [InlineData(".ssa", SubtitleOutputMode.MatchSource, new[] { ".ssa" })]
    [InlineData(".ssa", SubtitleOutputMode.Both, new[] { ".ssa", ".srt" })]
    [InlineData(".srt", SubtitleOutputMode.MatchSource, new[] { ".srt" })]
    [InlineData(".srt", SubtitleOutputMode.AssOnly, new[] { ".srt" })]
    [InlineData(".srt", SubtitleOutputMode.Both, new[] { ".srt" })]
    public void GetRequiredOutputFormats_ReturnsExpectedFormats(
        string sourceFormat,
        SubtitleOutputMode outputMode,
        string[] expectedFormats)
    {
        var formats = SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, outputMode);

        Assert.Equal(expectedFormats, formats.ToArray());
    }
}
