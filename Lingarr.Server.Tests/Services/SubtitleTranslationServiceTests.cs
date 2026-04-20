using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleTranslationServiceTests
{
    [Fact]
    public async Task ProcessSubtitleBatch_WhenProviderIsUnavailable_ShouldLogProviderUnavailabilityForDeferredRepair()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();

        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException(
                "Gemini is temporarily unavailable. Retry limit reached.",
                new HttpRequestException(
                    "Batch translation using Gemini API failed with status 503 (ServiceUnavailable). Response: {\"error\":{\"status\":\"UNAVAILABLE\"}}",
                    null,
                    HttpStatusCode.ServiceUnavailable)));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var currentBatch = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["Hello there"],
                PlaintextLines = ["Hello there"]
            }
        };

        var result = await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: false,
            collectFailures: true,
            fileIdentifier: "S02E10",
            batchNumber: 1,
            totalBatches: 1,
            cancellationToken: CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].Position);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("translation provider unavailability")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenPreservingAssFormatting_KeepsAssLineStructure()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();

        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "{\\an7\\pos(100,200)}To jest bardzo dlugi napis na znaku\\NWejscie wzbronione"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var currentBatch = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7\\pos(100,200)}A very long sign text\\NDo not enter"],
                PlaintextLines = ["A very long sign text Do not enter"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, currentBatch[0].TranslatedLines.Count);
        Assert.Equal("{\\an7\\pos(100,200)}To jest bardzo dlugi napis na znaku", currentBatch[0].TranslatedLines[0]);
        Assert.Equal("Wejscie wzbronione", currentBatch[0].TranslatedLines[1]);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenPreservingAssFormattingAndStripEnabled_UsesTaggedInput()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();
        List<BatchSubtitleItem>? capturedBatchItems = null;

        batchServiceMock
            .Setup(s => s.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> items, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatchItems = items;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "{\\an7\\pos(100,200)}Przetlumaczony tekst\\NWiersz drugi"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var currentBatch = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7\\pos(100,200)}A very long sign text\\NDo not enter"],
                PlaintextLines = ["A very long sign text Do not enter"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: true,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedBatchItems);
        Assert.Single(capturedBatchItems!);
        Assert.Equal("{\\an7\\pos(100,200)}A very long sign text\\NDo not enter", capturedBatchItems![0].Line);
        Assert.Equal("{\\an7\\pos(100,200)}Przetlumaczony tekst", currentBatch[0].TranslatedLines[0]);
        Assert.Equal("Wiersz drugi", currentBatch[0].TranslatedLines[1]);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenPreservingAssFormattingAndLineIsMeaningless_KeepsOriginalTaggedLine()
    {
        var translationServiceMock = new Mock<ITranslationService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(s => s.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var translationRequest = new TranslationRequest
        {
            Id = 1,
            Title = "Episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = Core.Enum.MediaType.Show,
            Status = Core.Enum.TranslationStatus.Pending
        };

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7\\pos(100,200)}z"],
                PlaintextLines = ["z"]
            }
        };

        var result = await service.TranslateSubtitles(
            subtitles,
            translationRequest,
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        Assert.Single(result[0].TranslatedLines);
        Assert.Equal("{\\an7\\pos(100,200)}z", result[0].TranslatedLines[0]);
        translationServiceMock.VerifyNoOtherCalls();
    }
}
