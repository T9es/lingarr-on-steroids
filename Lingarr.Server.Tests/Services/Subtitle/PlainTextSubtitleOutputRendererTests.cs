using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class PlainTextSubtitleOutputRendererTests
{
    [Theory]
    [InlineData("WWWWWWW")]
    [InlineData("1234567")]
    public void ShouldSkipSubtitle_SkipsSingleTokenVisualDebris(string text)
    {
        var lines = PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(text);

        Assert.True(PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(lines));
    }

    [Theory]
    [InlineData("Unison")]
    [InlineData("PRESENTS")]
    public void ShouldSkipSubtitle_KeepsWordLikeCredits(string text)
    {
        var lines = PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(text);

        Assert.False(PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(lines));
    }
}
