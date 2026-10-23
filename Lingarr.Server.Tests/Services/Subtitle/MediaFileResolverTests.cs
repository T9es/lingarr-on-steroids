using System;
using System.IO;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class MediaFileResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public MediaFileResolverTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "lingarr-media-file-resolver-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Resolve_ReturnsMediaFile_WhenStoredNameHasNoExtension()
    {
        var fileName = "Show (2020) - S01E01 - Pilot [WEBDL-1080p][x264]-GROUP";
        var expectedPath = CreateFile(fileName + ".mkv");

        var resolved = MediaFileResolver.Resolve(_tempDirectory, fileName);

        Assert.Equal(expectedPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsMediaFile_WhenStoredNameKeepsItsExtension()
    {
        var fileName = "Movie (2024) [WEBDL-1080p][x264]-GROUP.mkv";
        var expectedPath = CreateFile(fileName);

        var resolved = MediaFileResolver.Resolve(_tempDirectory, fileName);

        Assert.Equal(expectedPath, resolved);
    }

    [Fact]
    public void Resolve_DoesNotStripMovieYear_WhenFileNameIsExtensionLess()
    {
        var fileName = "Movie.Name.2024";
        var expectedPath = CreateFile(fileName + ".mkv");

        var resolved = MediaFileResolver.Resolve(_tempDirectory, fileName);

        Assert.Equal(expectedPath, resolved);
    }

    [Fact]
    public void Resolve_IgnoresNonMediaFilesWithTheSameBaseName()
    {
        CreateFile("Episode 01.en.srt");
        var expectedPath = CreateFile("Episode 01.mkv");

        var resolved = MediaFileResolver.Resolve(_tempDirectory, "Episode 01");

        Assert.Equal(expectedPath, resolved);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNoMediaFileMatches()
    {
        CreateFile("Other Episode.mkv");

        var resolved = MediaFileResolver.Resolve(_tempDirectory, "Episode 01");

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(_tempDirectory, "missing");

        var resolved = MediaFileResolver.Resolve(missingDirectory, "Episode 01");

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenFileNameIsMissing()
    {
        Assert.Null(MediaFileResolver.Resolve(_tempDirectory, null));
        Assert.Null(MediaFileResolver.Resolve(_tempDirectory, string.Empty));
    }

    private string CreateFile(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
