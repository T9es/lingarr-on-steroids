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

    [Theory]
    [InlineData("subrip", ".srt")]
    [InlineData("mov_text", ".srt")]
    [InlineData("webvtt", ".vtt")]
    [InlineData("ass", ".ass")]
    [InlineData("ssa", ".ssa")]
    public void NormalizeFormat_WithEmbeddedCodecNames_ReturnsExpectedExtension(string input, string expectedFormat)
    {
        var normalized = SubtitleOutputModeHelper.NormalizeFormat(input);

        Assert.Equal(expectedFormat, normalized);
    }

    [Theory]
    [InlineData("subrip", SubtitleOutputMode.MatchSource, new[] { ".srt" })]
    [InlineData("mov_text", SubtitleOutputMode.MatchSource, new[] { ".srt" })]
    [InlineData("webvtt", SubtitleOutputMode.MatchSource, new[] { ".vtt" })]
    [InlineData("ass", SubtitleOutputMode.Both, new[] { ".ass", ".srt" })]
    [InlineData("ssa", SubtitleOutputMode.SrtOnly, new[] { ".srt" })]
    public void GetRequiredOutputFormats_WithEmbeddedCodecNames_ReturnsExpectedFormats(
        string sourceFormat,
        SubtitleOutputMode outputMode,
        string[] expectedFormats)
    {
        var formats = SubtitleOutputModeHelper.GetRequiredOutputFormats(sourceFormat, outputMode);

        Assert.Equal(expectedFormats, formats.ToArray());
    }
}
