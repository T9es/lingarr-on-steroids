using System.IO;
using System.Text;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class MkvEmbeddingServiceTests
{
    [Fact]
    public void CreateTempOutputPath_WithNearLimitMkvName_StaysWithinFilenameLimit()
    {
        var mkvPath = $"/media/{new string('a', 250)}.mkv";

        var tempOutputPath = MkvEmbeddingService.CreateTempOutputPath(mkvPath);

        Assert.Equal(".mkv", Path.GetExtension(tempOutputPath));
        Assert.True(
            Encoding.UTF8.GetByteCount(Path.GetFileName(tempOutputPath)) <= 255,
            "Temporary merged MKV filename should stay within the ext4 filename byte limit.");
    }
}
