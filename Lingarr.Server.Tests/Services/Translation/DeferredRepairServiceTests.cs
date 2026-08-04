using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class DeferredRepairServiceTests
{
    [Fact]
    public void BuildContextualRepairBatch_UsesProviderVisibleTextAndPreservesVisibleNewlines()
    {
        var service = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an8}Line one\\NLine two"],
                PlaintextLines = ["Line one Line two"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            },
            new()
            {
                Position = 2,
                Lines = ["<i>Context</i>"],
                PlaintextLines = ["Context"]
            }
        };

        var providerTextByPosition = new Dictionary<int, string>
        {
            [1] = BuildAssProviderVisibleText("{\\an8}Line one\\NLine two"),
            [2] = BuildInlineProviderVisibleText("<i>Context</i>")
        };

        var batch = service.BuildContextualRepairBatch(
            [new RepairItem { Position = 1, OriginalLine = providerTextByPosition[1], OriginalBatchIndex = 1 }],
            subtitles,
            contextRadius: 1,
            providerTextByPosition);

        var failedItem = Assert.Single(batch.Items, item => item.Position == 1);
        Assert.Equal("Line one\nLine two", failedItem.Line);
        Assert.DoesNotContain("{", failedItem.Line, StringComparison.Ordinal);

        var contextItem = Assert.Single(batch.Items, item => item.Position == 2);
        Assert.Equal("Context", contextItem.Line);
        Assert.DoesNotContain("<", contextItem.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextualRepairBatch_WhenProviderVisibleTextMissing_FallsBackToPlaintextWithNewlines()
    {
        var service = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 10,
                Lines = ["{\\an8}First", "Second"],
                PlaintextLines = ["First", "Second"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        var batch = service.BuildContextualRepairBatch(
            [new RepairItem { Position = 10, OriginalLine = "First\nSecond", OriginalBatchIndex = 1 }],
            subtitles,
            contextRadius: 0,
            providerVisibleTextByPosition: new Dictionary<int, string>());

        var failedItem = Assert.Single(batch.Items);
        Assert.Equal("First\nSecond", failedItem.Line);
    }

    [Fact]
    public async Task ExecuteRepairAsync_SendsOnlyFailedRepresentativesWithContext_AndIgnoresExtraResults()
    {
        var fallbackServiceMock = new Mock<IBatchFallbackService>();
        List<BatchSubtitleItem>? capturedBatch = null;

        fallbackServiceMock
            .Setup(service => service.TranslateWithFallbackAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, IBatchTranslationService _, string _, string _, int _, string _, int _, int _, CancellationToken _) =>
            {
                capturedBatch = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "context must not be applied",
                [2] = "repaired",
                [3] = "context must not be applied"
            });

        var repairBatch = new ContextualRepairBatch
        {
            Items =
            [
                new BatchSubtitleItem { Position = 1, Line = "Before" },
                new BatchSubtitleItem { Position = 2, Line = "Failed source" },
                new BatchSubtitleItem { Position = 3, Line = "After" }
            ],
            FailedPositions = [2],
            Ranges = [new ContextRange(1, 3)]
        };

        var service = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        var result = await service.ExecuteRepairAsync(
            repairBatch,
            Mock.Of<IBatchTranslationService>(),
            fallbackServiceMock.Object,
            "en",
            "pl",
            batchSize: 10,
            maxRetries: 1,
            fileIdentifier: "test",
            CancellationToken.None);

        var request = Assert.Single(capturedBatch!);
        var contextualRequest = Assert.IsType<ContextualBatchSubtitleItem>(request);
        Assert.Equal(2, contextualRequest.Position);
        Assert.Equal(new[] { "Before" }, contextualRequest.PreContext);
        Assert.Equal(new[] { "After" }, contextualRequest.PostContext);
        Assert.Single(result);
        Assert.Equal("repaired", result[2]);
        Assert.DoesNotContain(1, result.Keys);
        Assert.DoesNotContain(3, result.Keys);
    }

    [Fact]
    public async Task ExecuteRepairAsync_BatchesMultipleFailedItemsPerApiCall()
    {
        var fallbackServiceMock = new Mock<IBatchFallbackService>();
        var calls = new List<List<BatchSubtitleItem>>();

        fallbackServiceMock
            .Setup(service => service.TranslateWithFallbackAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, IBatchTranslationService _, string _, string _, int _, string _, int _, int _, CancellationToken _) =>
            {
                calls.Add(batch);
            })
            .Returns((List<BatchSubtitleItem> batch, IBatchTranslationService _, string _, string _, int _, string _, int _, int _, CancellationToken _) =>
                Task.FromResult(batch.ToDictionary(item => item.Position, item => $"t{item.Position}")));

        var positions = Enumerable.Range(1, 250).ToList();
        var repairBatch = new ContextualRepairBatch
        {
            Items = positions.Select(position => new BatchSubtitleItem { Position = position, Line = $"L{position}" }).ToList(),
            FailedPositions = positions.ToHashSet(),
            Ranges = [new ContextRange(1, 250)]
        };

        var service = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        var result = await service.ExecuteRepairAsync(
            repairBatch,
            Mock.Of<IBatchTranslationService>(),
            fallbackServiceMock.Object,
            "en",
            "pl",
            batchSize: 100,
            maxRetries: 1,
            fileIdentifier: "test",
            CancellationToken.None);

        // 250 failed items split into 100/100/50 -> exactly 3 API calls, not 250
        Assert.Equal(3, calls.Count);
        Assert.Equal([100, 100, 50], calls.Select(call => call.Count).ToArray());
        Assert.Equal(250, result.Count);
    }

    [Fact]
    public async Task ExecuteRepairAsync_WhenRetriesAreExhausted_ReturnsMissingTranslationData()
    {
        var fallbackServiceMock = new Mock<IBatchFallbackService>();
        fallbackServiceMock
            .Setup(service => service.TranslateWithFallbackAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());

        var repairBatch = new ContextualRepairBatch
        {
            Items =
            [
                new BatchSubtitleItem { Position = 8, Line = "Failed source" }
            ],
            FailedPositions = [8],
            Ranges = [new ContextRange(8, 8)]
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() =>
            new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>()).ExecuteRepairAsync(
                repairBatch,
                Mock.Of<IBatchTranslationService>(),
                fallbackServiceMock.Object,
                "en",
                "pl",
                batchSize: 1,
                maxRetries: 0,
                fileIdentifier: "test",
                CancellationToken.None));

        var missingCue = Assert.Single(exception.MissingCues);
        Assert.Equal(8, missingCue.Position);
        Assert.Equal("Failed source", missingCue.SourceText);
    }

    private static string BuildAssProviderVisibleText(string line)
    {
        var structure = new SubtitleTextStructure(
            SubtitleStructureMode.Ass,
            [line],
            new AssTextStructureParser().Parse([line]));
        return structure.ProviderVisibleText;
    }

    private static string BuildInlineProviderVisibleText(string line)
    {
        var structure = new SubtitleTextStructure(
            SubtitleStructureMode.InlineMarkup,
            [line],
            new InlineMarkupStructureParser().Parse([line]));
        return structure.ProviderVisibleText;
    }
}
