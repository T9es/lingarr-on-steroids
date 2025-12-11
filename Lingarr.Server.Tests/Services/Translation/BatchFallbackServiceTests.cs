using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class BatchFallbackServiceTests
{
    private readonly Mock<ILogger<BatchFallbackService>> _loggerMock = new();

    [Fact]
    public async Task TranslateWithFallbackAsync_PartialChunkResults_ShouldRetryMissingItems()
    {
        // Arrange
        var batch = new List<BatchSubtitleItem>
        {
            new() { Position = 1, Line = "Line 1" },
            new() { Position = 2, Line = "Line 2" },
            new() { Position = 3, Line = "Line 3" },
            new() { Position = 4, Line = "Line 4" }
        };

        var batchServiceMock = new Mock<IBatchTranslationService>();

        // First call with full batch returns only a subset of translations (simulating partial failure)
        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.Is<List<BatchSubtitleItem>>(b => b.Count == 4),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> b, string _, string _, CancellationToken _) =>
                new Dictionary<int, string>
                {
                    { b[0].Position, "T1" },
                    { b[1].Position, "T2" }
                    // Positions 3 and 4 are missing
                });

        // Subsequent calls for single-item chunks succeed fully
        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.Is<List<BatchSubtitleItem>>(b => b.Count == 1 && b[0].Position == 3),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> b, string _, string _, CancellationToken _) =>
                new Dictionary<int, string> { { b[0].Position, "T3" } });

        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.Is<List<BatchSubtitleItem>>(b => b.Count == 1 && b[0].Position == 4),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> b, string _, string _, CancellationToken _) =>
                new Dictionary<int, string> { { b[0].Position, "T4" } });

        var service = new BatchFallbackService(_loggerMock.Object);

        // Act
        var result = await service.TranslateWithFallbackAsync(
            batch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "es",
            maxSplitAttempts: 3,
            fileIdentifier: "TestFile",
            batchNumber: 1,
            totalBatches: 1,
            cancellationToken: CancellationToken.None);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal("T1", result[1]);
        Assert.Equal("T2", result[2]);
        Assert.Equal("T3", result[3]);
        Assert.Equal("T4", result[4]);

        // Verify that we first called with the full batch, then retried smaller chunks
        batchServiceMock.Verify(s => s.TranslateBatchAsync(
                It.Is<List<BatchSubtitleItem>>(b => b.Count == 4),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        batchServiceMock.Verify(s => s.TranslateBatchAsync(
                It.Is<List<BatchSubtitleItem>>(b => b.Count == 1 && (b[0].Position == 3 || b[0].Position == 4)),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_AlwaysMissingItem_ShouldThrowAfterAllAttempts()
    {
        // Arrange
        var batch = new List<BatchSubtitleItem>
        {
            new() { Position = 1, Line = "Line 1" },
            new() { Position = 2, Line = "Line 2" }
        };

        var batchServiceMock = new Mock<IBatchTranslationService>();

        // Always return empty translations for all items in any chunk,
        // simulating a downstream service that never returns usable text
        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> chunk, string _, string _, CancellationToken _) =>
            {
                return chunk.ToDictionary(item => item.Position, _ => string.Empty);
            });

        var service = new BatchFallbackService(_loggerMock.Object);

        // Act / Assert
        await Assert.ThrowsAsync<TranslationException>(() => service.TranslateWithFallbackAsync(
            batch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "es",
            maxSplitAttempts: 2,
            fileIdentifier: "TestFile",
            batchNumber: 1,
            totalBatches: 1,
            cancellationToken: CancellationToken.None));

        // We should have attempted translation at least once
        batchServiceMock.Verify(s => s.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
