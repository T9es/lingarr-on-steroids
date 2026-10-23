using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public async Task GetBlockedMedia_ReturnsOkWithPagedItems()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.GetBlockedMediaAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<TranslationState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockedMediaPageResponse
            {
                Items = new List<BlockedMediaItemResponse>
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
                },
                TotalCount = 7,
                PageNumber = 2,
                PageSize = 50,
                CountsByState = new Dictionary<string, int>
                {
                    ["OcrBlocked"] = 1,
                    ["AwaitingSource"] = 6
                }
            });

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.GetBlockedMedia(
            page: 2,
            pageSize: 50,
            state: TranslationState.OcrBlocked,
            cancellationToken: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BlockedMediaPageResponse>(okResult.Value);
        var item = Assert.Single(payload.Items);
        Assert.Equal(42, item.MediaId);
        Assert.Equal("episode", item.MediaType);
        Assert.Equal(TranslationState.OcrBlocked, item.TranslationState);
        Assert.Equal(2, item.StreamIndex);
        Assert.Equal(31, item.OcrQualityScore);
        Assert.Equal(7, payload.TotalCount);
        Assert.Equal(2, payload.PageNumber);
        Assert.Equal(1, payload.CountsByState["OcrBlocked"]);

        blockedMediaServiceMock.Verify(
            service => service.GetBlockedMediaAsync(
                2,
                50,
                TranslationState.OcrBlocked,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBlockedMedia_ReturnsEmptyPageWhenNothingBlocked()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.GetBlockedMediaAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<TranslationState?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockedMediaPageResponse
            {
                Items = new List<BlockedMediaItemResponse>(),
                TotalCount = 0,
                PageNumber = 1,
                PageSize = 50
            });

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.GetBlockedMedia();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BlockedMediaPageResponse>(okResult.Value);
        Assert.Empty(payload.Items);
        Assert.Equal(0, payload.TotalCount);
    }

    [Fact]
    public async Task RecheckMedia_ReturnsOkWithRecomputedState()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.RecheckMediaAsync(
                MediaType.Episode,
                42,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockedMediaRecheckResponse
            {
                MediaId = 42,
                MediaType = "episode",
                State = TranslationState.Pending,
                EvaluatedAt = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)
            });

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.RecheckMedia("episode", 42);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BlockedMediaRecheckResponse>(okResult.Value);
        Assert.Equal(42, payload.MediaId);
        Assert.Equal(TranslationState.Pending, payload.State);
    }

    [Fact]
    public async Task RecheckMedia_WithInvalidMediaType_ReturnsBadRequest()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.RecheckMedia("show", 42);

        // Assert
        Assert.IsType<BadRequestObjectResult>(response.Result);
        blockedMediaServiceMock.Verify(
            service => service.RecheckMediaAsync(
                It.IsAny<MediaType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecheckMedia_WhenMediaDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var blockedMediaServiceMock = new Mock<IBlockedMediaService>();
        blockedMediaServiceMock
            .Setup(service => service.RecheckMediaAsync(
                MediaType.Movie,
                42,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlockedMediaRecheckResponse?)null);

        var controller = new BlockedMediaController(blockedMediaServiceMock.Object);

        // Act
        var response = await controller.RecheckMedia("movie", 42);

        // Assert
        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public void BlockedMediaPageResponse_SerializesAsCamelCaseEnvelopeWithStringEnums()
    {
        // Mirrors the app JSON options (ServiceCollectionExtensions.Configure):
        // JsonStringEnumConverter, camelCase properties, nulls omitted.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var payload = new BlockedMediaPageResponse
        {
            Items = new List<BlockedMediaItemResponse>
            {
                new()
                {
                    MediaId = 42,
                    MediaType = "episode",
                    Title = "Blocked Episode",
                    TranslationState = TranslationState.OcrBlocked,
                    StreamIndex = 0,
                    OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
                    OcrQualityScore = 75,
                    OcrMinimumQualityScore = 70,
                    OcrPassesCurrentThreshold = true,
                    OcrIssueSummary = "Quality score 75 meets the current minimum of 70.",
                    LastEvaluatedAt = new DateTime(2026, 5, 10, 21, 16, 50, DateTimeKind.Utc)
                }
            },
            TotalCount = 7,
            PageNumber = 2,
            PageSize = 50,
            CountsByState = new Dictionary<string, int>
            {
                ["OcrBlocked"] = 1,
                ["AwaitingSource"] = 6
            }
        };

        var json = JsonSerializer.Serialize(payload, options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(7, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("pageNumber").GetInt32());
        Assert.Equal(50, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("countsByState").GetProperty("OcrBlocked").GetInt32());
        Assert.Equal(6, root.GetProperty("countsByState").GetProperty("AwaitingSource").GetInt32());

        var item = root.GetProperty("items")[0];
        Assert.Equal(42, item.GetProperty("mediaId").GetInt32());
        Assert.Equal("episode", item.GetProperty("mediaType").GetString());
        Assert.Equal("OcrBlocked", item.GetProperty("translationState").GetString());
        Assert.Equal(0, item.GetProperty("streamIndex").GetInt32());
        Assert.Equal("BlockedLowQuality", item.GetProperty("ocrStatus").GetString());
        Assert.Equal(70, item.GetProperty("ocrMinimumQualityScore").GetInt32());
        Assert.True(item.GetProperty("ocrPassesCurrentThreshold").GetBoolean());
        Assert.False(item.TryGetProperty("failureMessage", out _));
    }
}
