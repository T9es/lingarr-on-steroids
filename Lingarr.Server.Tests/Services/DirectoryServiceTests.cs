using Lingarr.Server.Services;
using Xunit;
using System;
using System.Runtime.InteropServices;

namespace Lingarr.Server.Tests.Services;

public class DirectoryServiceTests
{
    [Fact]
    public void GetDirectoryContents_ShouldThrow_WhenAccessingForbiddenPath()
    {
        // Arrange
        var service = new DirectoryService();

        // This test only applies on Linux where blocked paths exist
        // On Windows, skip this test as the blocklist is Linux-specific
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return; // Skip test on non-Linux platforms
        }

        // Act & Assert - /etc exists on Linux and should be blocked
        Assert.Throws<UnauthorizedAccessException>(() => service.GetDirectoryContents("/etc"));
    }

    [Fact]
    public void GetDirectoryInfo_ShouldThrow_WhenAccessingForbiddenPath()
    {
        // Arrange
        var service = new DirectoryService();

        // This test only applies on Linux where blocked paths exist
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return; // Skip test on non-Linux platforms
        }

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => service.GetDirectoryInfo("/etc"));
    }
}

