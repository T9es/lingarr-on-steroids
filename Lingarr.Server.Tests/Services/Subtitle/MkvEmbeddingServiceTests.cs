using System;
using System.IO;
using System.Text;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public void WouldExceedPathLimit_OverLimitPath_ReturnsTrue()
    {
        var path = $"/media/{new string('a', 260)}.mkv";
        var result = new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance)
            .WouldExceedPathLimit(path);
        Assert.True(result);
    }

    [Fact]
    public void WouldExceedPathLimit_ShortPath_ReturnsFalse()
    {
        var path = "/media/short.mkv";

        var result = new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance)
            .WouldExceedPathLimit(path);

        Assert.False(result);
    }

    [Fact]
    public void WouldExceedPathLimit_NullOrEmpty_ReturnsFalse()
    {
        var service = new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance);

        Assert.False(service.WouldExceedPathLimit(null!));
        Assert.False(service.WouldExceedPathLimit(string.Empty));
    }

    [Fact]
    public void WouldExceedPathLimit_OldBackupSuffixPushesOverLimit()
    {
        var baseFilename = new string('a', 242);
        var mkvPath = $"/media/{baseFilename}.mkv";
        var backupPath = mkvPath + ".lingarr_backup";

        var service = new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance);
        Assert.False(service.WouldExceedPathLimit(mkvPath),
            "Base path should be within the limit");
        Assert.True(service.WouldExceedPathLimit(backupPath),
            "Appending .lingarr_backup should push the filename over the limit");
    }

    [Fact]
    public void WouldExceedPathLimit_GuidBackupWithinLimit()
    {
        var baseFilename = new string('a', 252);
        var mkvPath = $"/media/{baseFilename}.mkv";
        var service = new MkvEmbeddingService(NullLogger<MkvEmbeddingService>.Instance);
        Assert.True(service.WouldExceedPathLimit(mkvPath),
            "Original path should exceed the limit");
        var dir = Path.GetDirectoryName(mkvPath)!;
        var guidBackupPath = Path.Combine(dir, $".lingarr_swap_backup_{Guid.NewGuid():N}");
        Assert.True(
            Encoding.UTF8.GetByteCount(Path.GetFileName(guidBackupPath)) <= 255,
            "GUID-based backup filename should stay within the ext4 filename byte limit.");
    }

    [Fact]
    public void CreateTempOutputPath_WithLongMkvName_StaysWithinFilenameLimit()
    {
        var path = MkvEmbeddingService.CreateTempOutputPath($"/media/{new string('a', 254)}.mkv");

        Assert.Equal(".mkv", Path.GetExtension(path));
        Assert.True(
            Encoding.UTF8.GetByteCount(Path.GetFileName(path)) <= 255,
            "Temporary merged MKV filename should stay within the ext4 filename byte limit.");
    }
}
