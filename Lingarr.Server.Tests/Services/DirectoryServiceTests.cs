using Lingarr.Server.Services;
using Xunit;
using System;

namespace Lingarr.Server.Tests.Services;

public class DirectoryServiceTests
{
    [Fact]
    public void GetDirectoryContents_ShouldThrow_WhenAccessingForbiddenPath()
    {
        // Arrange
        // Current implementation has no dependencies
        var service = new DirectoryService();

        // Act & Assert
        // We expect this to fail initially (it won't throw)
        Assert.Throws<UnauthorizedAccessException>(() => service.GetDirectoryContents("/etc"));
    }
}
