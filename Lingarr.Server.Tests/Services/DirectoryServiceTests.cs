using System;
using System.IO;
using System.Runtime.InteropServices;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class DirectoryServiceTests
{
    private readonly DirectoryService _directoryService;
    private readonly string _testRoot;

    public DirectoryServiceTests()
    {
        _directoryService = new DirectoryService();
        // Use current directory (usually /app) which is not blocked
        _testRoot = Path.Combine(Directory.GetCurrentDirectory(), "LingarrDirectoryTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void GetDirectoryContents_ShouldSkipHiddenDirectories()
    {
        // Arrange
        var visibleDir = Path.Combine(_testRoot, "Visible");
        var hiddenDir = Path.Combine(_testRoot, ".Hidden");

        Directory.CreateDirectory(visibleDir);
        Directory.CreateDirectory(hiddenDir);

        // Act
        var contents = _directoryService.GetDirectoryContents(_testRoot);

        // Assert
        Assert.Contains(contents, i => i.Name == "Visible");
        Assert.DoesNotContain(contents, i => i.Name == ".Hidden");

        // Cleanup
        try { Directory.Delete(_testRoot, true); } catch {}
    }

    [Theory]
    [InlineData("/proc")]
    [InlineData("/etc")]
    [InlineData("/var/log")]
    public void ValidatePath_ShouldThrowOnBlockedPaths(string path)
    {
        // Only run on Linux to match the blocklist logic
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => _directoryService.GetDirectoryInfo(path));
    }
}
