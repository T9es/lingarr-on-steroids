using System;
using System.Collections.Generic;
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

    [Fact]
    public void BuildMkvMergeArguments_AppliesLanguageAndTrackNameToAppendedSubtitleInput()
    {
        var args = MkvEmbeddingService.BuildMkvMergeArguments(
            "/media/movie.mkv",
            "/tmp/translated.ass",
            "pol",
            "pol (Lingarr)",
            true,
            "/media/out.mkv",
            new List<int> { 10 });

        var argsString = string.Join(" ", args);
        Assert.Contains("/media/movie.mkv", argsString);
        Assert.Contains("/tmp/translated.ass", argsString);
        Assert.Contains("--language 0:pol", argsString);
        Assert.Contains("--track-name 0:pol (Lingarr)", argsString);

        var mkvIndex = args.IndexOf("/media/movie.mkv");
        var subtitleIndex = args.IndexOf("/tmp/translated.ass");
        var languageIndex = args.IndexOf("--language");
        var trackNameIndex = args.IndexOf("--track-name");

        Assert.True(mkvIndex >= 0, "MKV path should be present");
        Assert.True(subtitleIndex > mkvIndex, "Subtitle path should come after MKV path");
        Assert.True(languageIndex > mkvIndex && languageIndex < subtitleIndex,
            "Language flag should be between MKV and subtitle");
        Assert.True(trackNameIndex > mkvIndex && trackNameIndex < subtitleIndex,
            "Track name flag should be between MKV and subtitle");
    }
}
