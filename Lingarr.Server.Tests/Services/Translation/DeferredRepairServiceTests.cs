using System;
using System.Collections.Generic;
using System.Linq;
using Lingarr.Core.Entities;
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

        var failedItem = Assert.Single(batch.Items.Where(item => item.Position == 1));
        Assert.Equal("Line one\nLine two", failedItem.Line);
        Assert.DoesNotContain("{", failedItem.Line, StringComparison.Ordinal);

        var contextItem = Assert.Single(batch.Items.Where(item => item.Position == 2));
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
