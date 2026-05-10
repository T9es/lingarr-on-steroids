using System.Linq;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleServiceFilePathTests
{
    private readonly SubtitleService _service = new(NullLogger<SubtitleService>.Instance);

    [Fact]
    public void CreateFilePath_FromExternalSrt_ReplacesLanguageCorrectly()
    {
        var result = _service.CreateFilePath(
            @"C:\Movies\Film (2024)\Film (2024).eng.srt",
            "pl", "lingarr");

        Assert.Equal(@"C:\Movies\Film (2024)\Film (2024).pl.lingarr.srt", result);
    }

    [Fact]
    public void CreateFilePath_FromExternalSrt_WithCaption_ReplacesLanguageAndKeepsCaption()
    {
        var result = _service.CreateFilePath(
            @"C:\Movies\Film (2024)\Film (2024).eng.sdh.srt",
            "pl", "lingarr");

        Assert.Equal(@"C:\Movies\Film (2024)\Film (2024).pl.sdh.lingarr.srt", result);
    }

    [Fact]
    public void CreateFilePath_FromExternalSrt_SimpleCase()
    {
        var result = _service.CreateFilePath(
            @"C:\Movies\Movie\Movie.eng.srt",
            "pl", null!);

        Assert.Equal(@"C:\Movies\Movie\Movie.pl.srt", result);
    }

    [Fact]
    public void CreateFilePath_FromExternalSrt_WithExistingLanguageAndCaption()
    {
        var result = _service.CreateFilePath(
            @"C:\Movies\Movie\Movie.eng.sdh.srt",
            "pl", "lingarr");

        Assert.Equal(@"C:\Movies\Movie\Movie.pl.sdh.lingarr.srt", result);
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_Dts51_PreservesFullBaseName()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Transporter 2 (2005)\Transporter 2 (2005) [Bluray-1080p][DTS 5.1][x264]-Skazhutin.mkv",
            "pl", "lingarr", "ai", ".srt").ToList();

        Assert.All(paths, path =>
        {
            Assert.Contains("[DTS 5.1]", path);
            Assert.DoesNotContain("5.pl", path);
            Assert.Contains("Skazhutin", path);
        });
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_DtsHdMa51_PreservesFullBaseName()
    {
        var paths = _service.CreateFallbackPaths(
            @"\anime\The Legend of Korra (2012)\Season 01\The Legend of Korra (2012) - S01E01 - 001 - Welcome to Republic City [Bluray-1080p Remux][8bit][AVC][DTS-HD MA 5.1]-FraMeSToR.mkv",
            "pl", "lingarr", "ai", ".srt");

        Assert.All(paths, path =>
        {
            Assert.Contains("[DTS-HD MA 5.1]", path);
            Assert.DoesNotContain("5.pl", path);
            Assert.Contains("FraMeSToR", path);
        });
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_NoTag()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Film\Film (2024) [Bluray-1080p][EAC3 7.1][x264]-LoRD.mkv",
            "pl", "lingarr", "ai", ".srt").ToList();

        Assert.All(paths, path =>
        {
            Assert.Contains("[EAC3 7.1]", path);
            Assert.DoesNotContain("7.pl", path);
            Assert.Contains("LoRD", path);
        });
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_IncludesExpectedPaths()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Film\Film (2024) [DTS 5.1]-GRP.mkv",
            "pl", "lingarr", "ai", ".srt").ToList();

        Assert.Contains(
            @"C:\Movies\Film\Film (2024) [DTS 5.1]-GRP.pl.lingarr.srt",
            paths);
        Assert.Contains(
            @"C:\Movies\Film\Film (2024) [DTS 5.1]-GRP.pl.ai.srt",
            paths);
        Assert.Contains(
            @"C:\Movies\Film\Film (2024) [DTS 5.1]-GRP.pl.srt",
            paths);
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_WithForcedCaption()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Film\Film [DTS 5.1]-GRP.mkv",
            "pl", "lingarr", "ai", ".srt", "forced");

        Assert.All(paths, path =>
        {
            Assert.Contains("[DTS 5.1]", path);
            Assert.DoesNotContain("5.pl", path);
        });
    }

    [Fact]
    public void CreateFallbackPaths_FromVideoFile_Mp4Extension()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Movie\My Movie (2023) [WEBDL-1080p][AAC 2.0].mp4",
            "en", "lingarr", "ai", ".srt").ToList();

        Assert.All(paths, path =>
        {
            Assert.Contains("[AAC 2.0]", path);
            Assert.DoesNotContain("2.en", path);
            Assert.True(path.EndsWith(".srt"));
        });
    }

    [Fact]
    public void CreateFallbackPaths_FromExternalSrt_StillWorksCorrectly()
    {
        var paths = _service.CreateFallbackPaths(
            @"C:\Movies\Film\Film (2024) [Bluray-1080p][DTS 5.1][x264]-LeetHD.en.srt",
            "pl", "lingarr", "ai", ".srt").ToList();

        Assert.Contains(
            @"C:\Movies\Film\Film (2024) [Bluray-1080p][DTS 5.1][x264]-LeetHD.pl.lingarr.srt",
            paths);
    }
}