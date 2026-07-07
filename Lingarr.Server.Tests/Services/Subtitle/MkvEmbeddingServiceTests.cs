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

    [Fact]
    public void BuildMkvMergeArguments_UsesSingleNegatedCommaSeparatedSubtitleTrackList()
    {
        var args = MkvEmbeddingService.BuildMkvMergeArguments(
            "/media/movie.mkv",
            "/tmp/translated.srt",
            "pl",
            "pl (Lingarr)",
            false,
            "/media/out.mkv",
            new List<int> { 5, 7 });

        var subtitleTracksIndex = args.IndexOf("--subtitle-tracks");

        Assert.True(subtitleTracksIndex >= 0);
        Assert.Equal("!5,7", args[subtitleTracksIndex + 1]);
        Assert.DoesNotContain("!5", args);
        Assert.DoesNotContain("!7", args);
    }

    [Fact]
    public void FindLingarrTrackIdsToReplace_MatchesIso639TwoAndThreeLetterCodes()
    {
        var ids = MkvEmbeddingService.FindLingarrTrackIdsToReplace(
            BuildIdentifyJson(
                Track(2, "subtitles", "pol", null, "pl (Lingarr)", "S_TEXT/UTF8", "SubRip/SRT")),
            "pl",
            ".srt",
            "pl (Lingarr)");

        Assert.Equal([2], ids);
    }

    [Fact]
    public void FindLingarrTrackIdsToReplace_UsesTitleFallbackWhenLanguageMissing()
    {
        var ids = MkvEmbeddingService.FindLingarrTrackIdsToReplace(
            BuildIdentifyJson(
                Track(3, "subtitles", "und", null, "pl (Lingarr)", "S_TEXT/UTF8", "SubRip/SRT")),
            "pl",
            ".srt",
            "pl (Lingarr)");

        Assert.Equal([3], ids);
    }

    [Fact]
    public void FindLingarrTrackIdsToReplace_DoesNotRemoveNonLingarrTargetTracks()
    {
        var ids = MkvEmbeddingService.FindLingarrTrackIdsToReplace(
            BuildIdentifyJson(
                Track(4, "subtitles", "pol", null, "Polish Full", "S_TEXT/UTF8", "SubRip/SRT"),
                Track(5, "subtitles", "pol", null, "pl (Lingarr)", "S_TEXT/UTF8", "SubRip/SRT")),
            "pl",
            ".srt",
            "pl (Lingarr)");

        Assert.Equal([5], ids);
    }

    [Fact]
    public void FindLingarrTrackIdsToReplace_KeepsDifferentEmbeddedFormats()
    {
        var identifyJson = BuildIdentifyJson(
            Track(6, "subtitles", "pol", null, "pl (Lingarr)", "S_TEXT/UTF8", "SubRip/SRT"),
            Track(7, "subtitles", "pol", null, "pl (Lingarr)", "S_TEXT/ASS", "SubStationAlpha"));

        var srtIds = MkvEmbeddingService.FindLingarrTrackIdsToReplace(
            identifyJson,
            "pl",
            ".srt",
            "pl (Lingarr)");
        var assIds = MkvEmbeddingService.FindLingarrTrackIdsToReplace(
            identifyJson,
            "pl",
            ".ass",
            "pl (Lingarr)");

        Assert.Equal([6], srtIds);
        Assert.Equal([7], assIds);
    }

    private static string BuildIdentifyJson(params string[] tracks)
    {
        return $$"""
            {
              "tracks": [
                {{string.Join($",{Environment.NewLine}", tracks)}}
              ]
            }
            """;
    }

    private static string Track(
        int id,
        string type,
        string? language,
        string? languageIetf,
        string? title,
        string? codecId,
        string? codec)
    {
        return $$"""
                {
                  "id": {{id}},
                  "type": "{{type}}",
                  "codec": {{JsonString(codec)}},
                  "properties": {
                    "language": {{JsonString(language)}},
                    "language_ietf": {{JsonString(languageIetf)}},
                    "track_name": {{JsonString(title)}},
                    "codec_id": {{JsonString(codecId)}}
                  }
                }
            """;
    }

    private static string JsonString(string? value)
    {
        return value == null ? "null" : $"\"{value}\"";
    }
}
