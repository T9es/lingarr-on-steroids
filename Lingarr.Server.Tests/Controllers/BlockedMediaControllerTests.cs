using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class BlockedMediaControllerTests
{
    [Fact]
    public async Task GetBlockedMedia_ReturnsOkWithBlockedItems()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.GetBlockedMediaAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlockedMediaItemResponse>
            {
                new()
                {
                    MediaId = 42,
                    MediaType = "episode",
                    Title = "Blocked Episode",
                    TranslationState = TranslationState.OcrBlocked,
                    StreamIndex = 2,
                    OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
                    OcrQualityScore = 31,
                    OcrIssueSummary = "Too few cues detected."
                }
            });

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.GetBlockedMedia(limit: 200, cancellationToken: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<List<BlockedMediaItemResponse>>(okResult.Value);
        var item = Assert.Single(payload);
        Assert.Equal(42, item.MediaId);
        Assert.Equal("episode", item.MediaType);
        Assert.Equal(TranslationState.OcrBlocked, item.TranslationState);
        Assert.Equal(2, item.StreamIndex);
        Assert.Equal(31, item.OcrQualityScore);

        blockedMediaServiceMock.Verify(
            service => service.GetBlockedMediaAsync(200, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBlockedMedia_ReturnsEmptyListWhenNothingBlocked()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.GetBlockedMediaAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlockedMediaItemResponse>());

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.GetBlockedMedia(limit: 200, cancellationToken: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<List<BlockedMediaItemResponse>>(okResult.Value);
        Assert.Empty(payload);
    }
}
