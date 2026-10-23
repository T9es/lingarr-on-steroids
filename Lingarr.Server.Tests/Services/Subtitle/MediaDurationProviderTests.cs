using System;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class MediaDurationProviderTests
{
    [Fact]
    public async Task GetDurationSecondsAsync_ReturnsNullForBlankPath()
    {
        var provider = CreateProvider();

        Assert.Null(await provider.GetDurationSecondsAsync(" "));
    }

    [Fact]
    public async Task GetDurationSecondsAsync_ReturnsNullForMissingFile()
    {
        var provider = CreateProvider();
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "lingarr-duration-tests",
            Guid.NewGuid().ToString("N"),
            "missing.mkv");

        Assert.Null(await provider.GetDurationSecondsAsync(missingPath));
    }

    [Fact]
    public async Task GetDurationSecondsAsync_ReturnsNullInsteadOfThrowingForNonMediaFile()
    {
        var provider = CreateProvider();
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"lingarr-duration-tests-{Guid.NewGuid():N}.mkv");

        await File.WriteAllTextAsync(tempPath, "not a media file");
        try
        {
            Assert.Null(await provider.GetDurationSecondsAsync(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static MediaDurationProvider CreateProvider()
    {
        return new MediaDurationProvider(NullLogger<MediaDurationProvider>.Instance);
    }
}
