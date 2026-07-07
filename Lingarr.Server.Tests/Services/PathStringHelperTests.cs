using Lingarr.Server.Services;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class PathStringHelperTests
{
    [Theory]
    [InlineData(@"C:\media\custom\custom.movie.mkv", @"C:\media\custom")]
    [InlineData("/media/custom/custom.movie.mkv", "/media/custom")]
    [InlineData(@"relative\custom.movie.mkv", "relative")]
    public void GetDirectoryName_HandlesWindowsAndUnixSeparators(string path, string expected)
    {
        Assert.Equal(expected, PathStringHelper.GetDirectoryName(path));
    }

    [Theory]
    [InlineData(@"..\nested\episode.en.srt", "episode.en.srt")]
    [InlineData("../../nested/episode.en.srt", "episode.en.srt")]
    [InlineData("episode.en.srt", "episode.en.srt")]
    public void GetFileName_HandlesWindowsAndUnixSeparators(string path, string expected)
    {
        Assert.Equal(expected, PathStringHelper.GetFileName(path));
    }
}
