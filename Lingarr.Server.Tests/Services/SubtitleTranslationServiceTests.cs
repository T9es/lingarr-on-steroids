using System;
using System.Collections.Generic;
using System.Linq;
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
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
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
            .Setup(service => service.TranslateBatchAsync(
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
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("translation provider unavailability")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenProviderEchoesSourceText_CollectsFailuresWithoutTranslatedLines()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(item => item.Position, item => item.Line));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var currentBatch = new List<SubtitleItem>
        {
            Item(1, "Hello, my friend"),
            Item(2, "We need to go home"),
            Item(3, "This is very important"),
            Item(4, "Where is your sister?"),
            Item(5, "I cannot talk right now")
        };

        var result = await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: false,
            collectFailures: true,
            fileIdentifier: "S01E01",
            batchNumber: 1,
            totalBatches: 1,
            cancellationToken: CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], result.Select(item => item.Position));
        Assert.All(currentBatch, subtitle => Assert.Empty(subtitle.TranslatedLines));
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenProviderUsesWrongTargetLanguage_CollectsFailuresWithoutTranslatedLines()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();

        var japaneseLines = new Dictionary<int, string>
        {
            [1] = "静流は大好きなゲームを迷わず諦める",
            [2] = "ただ俺の負担にならないように",
            [3] = "それに彼女は生活を維持しようと必死で",
            [4] = "でも俺にも静流にしてやれることはある",
            [5] = "静かにしろよ、拉致するからな"
        };

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(japaneseLines);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var currentBatch = new List<SubtitleItem>
        {
            Item(1, "Shizuri will give up a game she loves without question"),
            Item(2, "just so she will not be a burden to me"),
            Item(3, "And she is doing her best to maintain our lifestyle"),
            Item(4, "but there are things I can do for Shizuri too"),
            Item(5, "Stay quiet so we can kidnap you")
        };

        var result = await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: false,
            collectFailures: true,
            fileIdentifier: "S01E16",
            batchNumber: 1,
            totalBatches: 1,
            cancellationToken: CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], result.Select(item => item.Position));
        Assert.All(currentBatch, subtitle => Assert.Empty(subtitle.TranslatedLines));
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderConfigurationFailure_ShouldFailFastWithoutDeferredRepair()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var batchFallbackMock = new Mock<IBatchFallbackService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairService = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException("API key not valid. Please pass a valid API key."));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            batchFallbackMock.Object,
            deferredRepairService);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["Hello there"],
                PlaintextLines = ["Hello there"]
            }
        };

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 10,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            batchSize: 100,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None));

        Assert.Contains("API key not valid", exception.Message, StringComparison.OrdinalIgnoreCase);
        batchFallbackMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenPreservingAssFormatting_ReconstructsFromVisibleProviderText()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();
        List<BatchSubtitleItem>? capturedBatchItems = null;

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatchItems = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "To jest bardzo dlugi napis na znaku\nWejscie wzbronione"
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

        Assert.NotNull(capturedBatchItems);
        Assert.Single(capturedBatchItems!);
        Assert.Equal("A very long sign text\nDo not enter", capturedBatchItems![0].Line);
        Assert.DoesNotContain("{", capturedBatchItems[0].Line, StringComparison.Ordinal);

        Assert.Single(currentBatch[0].TranslatedLines);
        Assert.Equal("{\\an7\\pos(100,200)}To jest bardzo dlugi napis na znaku\\NWejscie wzbronione", currentBatch[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenPreservingAssFormattingAndStripEnabled_UsesVisibleInputOnly()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();
        List<BatchSubtitleItem>? capturedBatchItems = null;

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
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
                [1] = "Przetlumaczony napis"
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
                Lines = ["{\\p1}m 0 0 l 12 12{\\p0}{\\an8}A sign"],
                PlaintextLines = ["A sign"],
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
        Assert.Equal("A sign", capturedBatchItems![0].Line);
        Assert.DoesNotContain("{", capturedBatchItems[0].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("m 0 0", capturedBatchItems[0].Line, StringComparison.Ordinal);
        Assert.Single(currentBatch[0].TranslatedLines);
        Assert.Equal("{\\p1}m 0 0 l 12 12{\\p0}{\\an8}Przetlumaczony napis", currentBatch[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenPreservingAssFormattingAndLineIsMeaningless_KeepsOriginalTaggedLine()
    {
        var translationServiceMock = new Mock<ITranslationService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
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
            MediaType = Lingarr.Core.Enum.MediaType.Show,
            Status = Lingarr.Core.Enum.TranslationStatus.Pending
        };

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7\\pos(100,200)}z"],
                PlaintextLines = ["z"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
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

    [Fact]
    public async Task TranslateSubtitles_WhenCachedSourceEchoIsInvalidatedAndRepaired()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var savedCheckpoints = new List<TranslationCheckpoint>();
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = 108,
            SourceFingerprint = "non-batch-echo",
            Translations = new Dictionary<int, string>
            {
                [1] = "Life isn't over yet!"
            }
        };

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Życie jeszcze się nie skończyło!");
        checkpointServiceMock
            .Setup(service => service.LoadAsync(108, "non-batch-echo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointServiceMock
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>()))
            .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoints.Add(saved))
            .Returns(Task.CompletedTask);
        checkpointServiceMock
            .Setup(service => service.SaveTranslationAsync(
                108,
                "non-batch-echo",
                1,
                "Życie jeszcze się nie skończyło!",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);

        var result = await service.TranslateSubtitles(
            [Item(1, "Life isn't over yet!")],
            new TranslationRequest
            {
                Id = 108,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending,
                SourceSnapshotFingerprint = "non-batch-echo"
            },
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("Życie jeszcze się nie skończyło!", result[0].TranslatedLines[0]);
        translationServiceMock.Verify(service => service.TranslateAsync(
            "Life isn't over yet!",
            "en",
            "pl",
            It.IsAny<List<string>?>(),
            It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var savedCheckpoint = Assert.Single(savedCheckpoints);
        Assert.DoesNotContain(1, savedCheckpoint.Translations.Keys);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenCachedTranslationUsesWrongTargetLanguageIsInvalidatedAndRepaired()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var savedCheckpoints = new List<TranslationCheckpoint>();
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = 109,
            SourceFingerprint = "non-batch-language",
            Translations = new Dictionary<int, string>
            {
                [1] = "This translation is ready for review."
            }
        };

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("この翻訳は確認の準備ができています。");
        checkpointServiceMock
            .Setup(service => service.LoadAsync(109, "non-batch-language", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointServiceMock
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>()))
            .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoints.Add(saved))
            .Returns(Task.CompletedTask);
        checkpointServiceMock
            .Setup(service => service.SaveTranslationAsync(
                109,
                "non-batch-language",
                1,
                "この翻訳は確認の準備ができています。",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);

        var result = await service.TranslateSubtitles(
            [Item(1, "The translation is ready for review.")],
            new TranslationRequest
            {
                Id = 109,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "ja",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending,
                SourceSnapshotFingerprint = "non-batch-language"
            },
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal("この翻訳は確認の準備ができています。", result[0].TranslatedLines[0]);
        translationServiceMock.Verify(service => service.TranslateAsync(
            "The translation is ready for review.",
            "en",
            "ja",
            It.IsAny<List<string>?>(),
            It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var savedCheckpoint = Assert.Single(savedCheckpoints);
        Assert.DoesNotContain(1, savedCheckpoint.Translations.Keys);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenFreshProviderEchoesSource_ThrowsBeforeCheckpointOrApply()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This source sentence must be translated before approval.");
        checkpointServiceMock
            .Setup(service => service.LoadAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationCheckpoint?)null);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);
        var subtitles = new List<SubtitleItem>
        {
            Item(1, "This source sentence must be translated before approval.")
        };

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateSubtitles(
            subtitles,
            new TranslationRequest
            {
                Id = 112,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("source", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("echo", exception.Message, StringComparison.OrdinalIgnoreCase);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            112,
            It.IsAny<string>(),
            1,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenFreshProviderUsesWrongTargetLanguage_ThrowsBeforeCheckpointOrApply()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("\u3053\u306e\u7ffb\u8a33\u306f\u65e5\u672c\u8a9e\u3067\u3059\u3002");
        checkpointServiceMock
            .Setup(service => service.LoadAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationCheckpoint?)null);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);
        var subtitles = new List<SubtitleItem>
        {
            Item(1, "This translation is ready for final review.")
        };

        var exception = await Assert.ThrowsAsync<TranslationException>(() => service.TranslateSubtitles(
            subtitles,
            new TranslationRequest
            {
                Id = 113,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("wrong target language", exception.Message, StringComparison.OrdinalIgnoreCase);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            113,
            It.IsAny<string>(),
            1,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_WhenPlainSubtitleContainsAssSyntax_UsesVisibleInputOnly()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var batchServiceMock = new Mock<IBatchTranslationService>();
        List<BatchSubtitleItem>? capturedBatchItems = null;

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatchItems = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Przetlumaczony napis"
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
                Lines = ["{\\an7\\pos(100,200)}A sign"],
                PlaintextLines = ["A sign"]
            }
        };

        await service.ProcessSubtitleBatch(
            currentBatch,
            batchServiceMock.Object,
            sourceLanguage: "en",
            targetLanguage: "pl",
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedBatchItems);
        Assert.Single(capturedBatchItems!);
        Assert.Equal("A sign", capturedBatchItems![0].Line);
        Assert.DoesNotContain("{", capturedBatchItems[0].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("\\an", capturedBatchItems[0].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("\\pos", capturedBatchItems[0].Line, StringComparison.Ordinal);
        Assert.Single(currentBatch[0].TranslatedLines);
        Assert.Equal("{\\an7\\pos(100,200)}Przetlumaczony napis", currentBatch[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderVisibleTextIsDuplicatedAcrossBatches_SendsOnlyRepresentative()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedBatches = new List<List<BatchSubtitleItem>>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatches.Add(batch.Select(item => new BatchSubtitleItem
                {
                    Position = item.Position,
                    Line = item.Line
                }).ToList());
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Franszczu"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7}Fran"],
                PlaintextLines = ["Fran"]
            },
            new()
            {
                Position = 2,
                Lines = ["{\\an8}Fran"],
                PlaintextLines = ["Fran"]
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 100,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "immediate",
            cancellationToken: CancellationToken.None);

        Assert.Single(capturedBatches);
        var sentBatch = Assert.Single(capturedBatches[0]);
        Assert.Equal(1, sentBatch.Position);
        Assert.Equal("Fran", sentBatch.Line);
        Assert.Equal("{\\an7}Franszczu", result[0].TranslatedLines[0]);
        Assert.Equal("{\\an8}Franszczu", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenCachedEchoRepresentativeHasDuplicatePositions_RepairsItAndPreservesValidCache()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedBatches = new List<List<BatchSubtitleItem>>();
        var savedCheckpoints = new List<TranslationCheckpoint>();
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = 110,
            SourceFingerprint = "batch-checkpoint",
            Translations = new Dictionary<int, string>
            {
                [243] = "Life isn't over yet!",
                [244] = "Life isn't over yet!",
                [245] = "Musimy teraz wyjść."
            }
        };

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatches.Add(batch);
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [243] = "Życie jeszcze się nie skończyło!"
            });
        checkpointServiceMock
            .Setup(service => service.LoadAsync(110, "batch-checkpoint", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointServiceMock
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>()))
            .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoints.Add(saved))
            .Returns(Task.CompletedTask);
        checkpointServiceMock
            .Setup(service => service.SaveTranslationAsync(
                110,
                "batch-checkpoint",
                243,
                "Życie jeszcze się nie skończyło!",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);

        var result = await service.TranslateSubtitlesBatch(
            [
                Item(243, "Life isn't over yet!"),
                Item(244, "Life isn't over yet!"),
                Item(245, "We have to leave now.")
            ],
            new TranslationRequest
            {
                Id = 110,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending,
                SourceSnapshotFingerprint = "batch-checkpoint"
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 3,
            batchRetryMode: "none",
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        var sentItem = Assert.Single(sentBatch);
        Assert.Equal(243, sentItem.Position);
        Assert.Equal("Life isn't over yet!", sentItem.Line);
        Assert.Equal("Życie jeszcze się nie skończyło!", result[0].TranslatedLines[0]);
        Assert.Equal("Życie jeszcze się nie skończyło!", result[1].TranslatedLines[0]);
        Assert.Equal("Musimy teraz wyjść.", result[2].TranslatedLines[0]);
        var savedCheckpoint = Assert.Single(savedCheckpoints);
        Assert.DoesNotContain(243, savedCheckpoint.Translations.Keys);
        Assert.DoesNotContain(244, savedCheckpoint.Translations.Keys);
        Assert.Equal("Musimy teraz wyjść.", savedCheckpoint.Translations[245]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDuplicateCheckpointAliasesHaveMixedValidity_UsesDeterministicCanonicalValues()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedBatches = new List<List<BatchSubtitleItem>>();
        var savedCheckpoints = new List<TranslationCheckpoint>();
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = 114,
            SourceFingerprint = "mixed-duplicate-checkpoint",
            Translations = new Dictionary<int, string>
            {
                [1] = "Zycie jeszcze sie nie skonczylo.",
                [2] = "Life isn't over yet!",
                [3] = "We have to leave now.",
                [4] = "Musimy teraz wyjsc.",
                [999] = "Healthy unrelated translation"
            }
        };

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedBatches.Add(batch);
            })
            .ReturnsAsync(new Dictionary<int, string>());
        checkpointServiceMock
            .Setup(service => service.LoadAsync(114, "mixed-duplicate-checkpoint", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointServiceMock
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>()))
            .Callback<TranslationCheckpoint, CancellationToken>((saved, _) => savedCheckpoints.Add(saved))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);

        var result = await service.TranslateSubtitlesBatch(
            [
                Item(1, "Life isn't over yet!"),
                Item(2, "Life isn't over yet!"),
                Item(3, "We have to leave now."),
                Item(4, "We have to leave now.")
            ],
            new TranslationRequest
            {
                Id = 114,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending,
                SourceSnapshotFingerprint = "mixed-duplicate-checkpoint"
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 4,
            batchRetryMode: "none",
            cancellationToken: CancellationToken.None);

        Assert.Empty(capturedBatches);
        Assert.Equal("Zycie jeszcze sie nie skonczylo.", result[0].TranslatedLines[0]);
        Assert.Equal("Zycie jeszcze sie nie skonczylo.", result[1].TranslatedLines[0]);
        Assert.Equal("Musimy teraz wyjsc.", result[2].TranslatedLines[0]);
        Assert.Equal("Musimy teraz wyjsc.", result[3].TranslatedLines[0]);
        var savedCheckpoint = Assert.Single(savedCheckpoints);
        Assert.Equal("Zycie jeszcze sie nie skonczylo.", savedCheckpoint.Translations[1]);
        Assert.DoesNotContain(2, savedCheckpoint.Translations.Keys);
        Assert.Equal("Musimy teraz wyjsc.", savedCheckpoint.Translations[3]);
        Assert.DoesNotContain(4, savedCheckpoint.Translations.Keys);
        Assert.Equal("Healthy unrelated translation", savedCheckpoint.Translations[999]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenOneFreshResultUsesWrongTargetLanguage_RejectsOnlyThatCueBeforePersistence()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(
                    item => item.Position,
                    item => item.Position == 1
                        ? "\u3053\u306e\u7ffb\u8a33\u306f\u65e5\u672c\u8a9e\u3067\u3059\u3002"
                        : $"Przetlumaczona linia {item.Position}"));
        checkpointServiceMock
            .Setup(service => service.LoadAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationCheckpoint?)null);
        checkpointServiceMock
            .Setup(service => service.SaveTranslationAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);
        var subtitles = Enumerable.Range(1, 5)
            .Select(position => Item(position, $"This is source subtitle line number {position}."))
            .ToList();

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 115,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 5,
            batchRetryMode: "none",
            cancellationToken: CancellationToken.None));

        Assert.Contains(1, exception.MissingCues.Select(cue => cue.Position));
        Assert.Empty(subtitles[0].TranslatedLines);
        Assert.Equal("Przetlumaczona linia 2", subtitles[1].TranslatedLines[0]);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            115,
            It.IsAny<string>(),
            1,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDedupedBatchContextIsEnabled_UsesOriginalSubtitleIndexes()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedPreContexts = new List<List<string>?>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((List<BatchSubtitleItem> _, string _, string _, List<string>? preContext, List<string>? _, CancellationToken _) =>
            {
                capturedPreContexts.Add(preContext?.ToList());
            })
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(
                    item => item.Position,
                    item => item.Line == "Before" ? "Przed" : "Franszczu"));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\p1}m 0 0 l 10 10{\\p0}"],
                PlaintextLines = [string.Empty],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Draw" }
            },
            new()
            {
                Position = 2,
                Lines = ["Before"],
                PlaintextLines = ["Before"]
            },
            new()
            {
                Position = 3,
                Lines = ["{\\an7}Fran"],
                PlaintextLines = ["Fran"]
            },
            new()
            {
                Position = 4,
                Lines = ["{\\an8}Fran"],
                PlaintextLines = ["Fran"]
            }
        };

        await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 103,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 1,
            batchRetryMode: "immediate",
            batchContextEnabled: true,
            batchContextBefore: 1,
            batchContextAfter: 0,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, capturedPreContexts.Count);
        Assert.Empty(capturedPreContexts[0]!);
        Assert.Equal(["Before"], capturedPreContexts[1]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenMostEntriesAreSkippedDrawingEvents_DoesNotSplitProgressIntoEmptyBatches()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var emittedProgress = new List<int>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Callback((TranslationRequest _, int progress) => emittedProgress.Add(progress))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Przetlumaczony napis"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7}A sign"],
                PlaintextLines = ["A sign"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            },
            new()
            {
                Position = 2,
                Lines = ["{\\p1}m 0 0 l 10 10{\\p0}"],
                PlaintextLines = [string.Empty],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Draw" }
            },
            new()
            {
                Position = 3,
                Lines = ["{\\p1}m 1 1 l 11 11{\\p0}"],
                PlaintextLines = [string.Empty],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Draw" }
            },
            new()
            {
                Position = 4,
                Lines = ["{\\p1}m 2 2 l 12 12{\\p0}"],
                PlaintextLines = [string.Empty],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Draw" }
            }
        };

        await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 101,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 1,
            batchRetryMode: "immediate",
            cancellationToken: CancellationToken.None);

        progressServiceMock.Verify(
            service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()),
            Times.Once);
        Assert.Single(emittedProgress);
        Assert.Equal(100, emittedProgress[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDeferredModeHasNoFailures_EmitsFinalProgress()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairServiceMock = new Mock<IDeferredRepairService>();
        var emittedProgress = new List<int>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Callback((TranslationRequest _, int progress) => emittedProgress.Add(progress))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Przetlumaczony napis"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            deferredRepairServiceMock.Object);

        await service.TranslateSubtitlesBatch(
            [
                new SubtitleItem
                {
                    Position = 1,
                    Lines = ["A sign"],
                    PlaintextLines = ["A sign"]
                }
            ],
            new TranslationRequest
            {
                Id = 104,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.Contains(95, emittedProgress);
        Assert.Equal(100, emittedProgress[^1]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDeferredRepairHasDuplicateFailures_RepairsRepresentativeAndFansOut()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairServiceMock = new Mock<IDeferredRepairService>();
        List<RepairItem>? capturedRepairItems = null;

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());

        deferredRepairServiceMock
            .Setup(service => service.BuildContextualRepairBatch(
                It.IsAny<List<RepairItem>>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<int, string>>()))
            .Callback((List<RepairItem> failedItems, List<SubtitleItem> _, int _, IReadOnlyDictionary<int, string> _) =>
            {
                capturedRepairItems = failedItems
                    .Select(item => new RepairItem
                    {
                        Position = item.Position,
                        OriginalLine = item.OriginalLine,
                        OriginalBatchIndex = item.OriginalBatchIndex
                    })
                    .ToList();
            })
            .Returns(new ContextualRepairBatch
            {
                Items =
                [
                    new BatchSubtitleItem { Position = 1, Line = "Fran" }
                ],
                FailedPositions = [1],
                Ranges = [new ContextRange(1, 1)]
            });

        deferredRepairServiceMock
            .Setup(service => service.ExecuteRepairAsync(
                It.IsAny<ContextualRepairBatch>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<IBatchFallbackService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Franszczu"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            deferredRepairServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an7}Fran"],
                PlaintextLines = ["Fran"]
            },
            new()
            {
                Position = 2,
                Lines = ["{\\an8}Fran"],
                PlaintextLines = ["Fran"]
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 102,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedRepairItems);
        var repairItem = Assert.Single(capturedRepairItems!);
        Assert.Equal(1, repairItem.Position);
        Assert.Equal("Fran", repairItem.OriginalLine);
        Assert.Equal("{\\an7}Franszczu", result[0].TranslatedLines[0]);
        Assert.Equal("{\\an8}Franszczu", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDeferredRepairReturnsSourceEcho_DoesNotApplyOrPersistIt()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairServiceMock = new Mock<IDeferredRepairService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());
        checkpointServiceMock
            .Setup(service => service.LoadAsync(111, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationCheckpoint?)null);
        deferredRepairServiceMock
            .Setup(service => service.BuildContextualRepairBatch(
                It.IsAny<List<RepairItem>>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<int, string>>()))
            .Returns((List<RepairItem> failedItems, List<SubtitleItem> _, int _, IReadOnlyDictionary<int, string> _) => new ContextualRepairBatch
            {
                Items = failedItems
                    .Select(item => new BatchSubtitleItem { Position = item.Position, Line = item.OriginalLine })
                    .ToList(),
                FailedPositions = failedItems.Select(item => item.Position).ToHashSet()
            });
        deferredRepairServiceMock
            .Setup(service => service.ExecuteRepairAsync(
                It.IsAny<ContextualRepairBatch>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<IBatchFallbackService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContextualRepairBatch _, IBatchTranslationService _, IBatchFallbackService _, string _, string _, int _, int _, string _, CancellationToken _) =>
                Enumerable.Range(1, 30)
                    .ToDictionary(
                        position => position,
                        position => position <= 2
                            ? $"Subtitle line number {position}"
                            : $"Przetłumaczona linia {position}"));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            deferredRepairServiceMock.Object,
            checkpointServiceMock.Object);

        var subtitles = Enumerable.Range(1, 30)
            .Select(position => Item(position, $"Subtitle line number {position}"))
            .ToList();

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 111,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 30,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            111,
            It.IsAny<string>(),
            1,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            111,
            It.IsAny<string>(),
            2,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(subtitles[0].TranslatedLines);
        Assert.Empty(subtitles[1].TranslatedLines);
        Assert.Equal("Przetłumaczona linia 3", subtitles[2].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDeferredRepairCannotResolveTranslatableCue_FailsInsteadOfPreservingSource()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairServiceMock = new Mock<IDeferredRepairService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());

        deferredRepairServiceMock
            .Setup(service => service.BuildContextualRepairBatch(
                It.IsAny<List<RepairItem>>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<int, string>>()))
            .Returns(new ContextualRepairBatch
            {
                Items =
                [
                    new BatchSubtitleItem { Position = 1, Line = "LISTEN, EVERYONE." }
                ],
                FailedPositions = [1],
                Ranges = [new ContextRange(1, 1)]
            });

        deferredRepairServiceMock
            .Setup(service => service.ExecuteRepairAsync(
                It.IsAny<ContextualRepairBatch>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<IBatchFallbackService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>());

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            deferredRepairServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            Item(1, "LISTEN, EVERYONE.")
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 105,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None));

        Assert.Contains("missing", exception.Message);
        Assert.All(exception.MissingCues, cue => Assert.False(cue.AutoApprovalEligible));
        Assert.Empty(subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderEchoesSource_ThrowsEligibleMissingTranslationException()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(item => item.Position, item => item.Line));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            Item(1, "Opening line one"),
            Item(2, "Opening line two"),
            Item(3, "Opening line three")
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 106,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 3,
            batchRetryMode: "none",
            cancellationToken: CancellationToken.None));

        Assert.Equal([1, 2, 3], exception.MissingCues.Select(cue => cue.Position));
        Assert.All(exception.MissingCues, cue => Assert.True(cue.AutoApprovalEligible));
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenResidualEchoedCuesAreWithinTolerance_PreservesSource()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairServiceMock = new Mock<IDeferredRepairService>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(
                    item => item.Position,
                    item => item.Position == 1 ? item.Line : $"Przetlumaczona linia {item.Position}"));

        deferredRepairServiceMock
            .Setup(service => service.BuildContextualRepairBatch(
                It.IsAny<List<RepairItem>>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<int, string>>()))
            .Returns(new ContextualRepairBatch
            {
                Items =
                [
                    new BatchSubtitleItem { Position = 1, Line = "Whoa, whoa, whoa." }
                ],
                FailedPositions = [1],
                Ranges = [new ContextRange(1, 1)]
            });

        deferredRepairServiceMock
            .Setup(service => service.ExecuteRepairAsync(
                It.IsAny<ContextualRepairBatch>(),
                It.IsAny<IBatchTranslationService>(),
                It.IsAny<IBatchFallbackService>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Whoa, whoa, whoa."
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            Mock.Of<IBatchFallbackService>(),
            deferredRepairServiceMock.Object);
        var subtitles = Enumerable.Range(1, 100)
            .Select(position => Item(position, position == 1 ? "Whoa, whoa, whoa." : $"Subtitle line number {position}"))
            .ToList();

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 107,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 100,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.Equal("Whoa, whoa, whoa.", result[0].TranslatedLines[0]);
        Assert.Equal("Przetlumaczona linia 2", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenBatchContextIsHuge_ClampsContextSentToProvider()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedBeforeCounts = new List<int>();
        var capturedAfterCounts = new List<int>();

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        batchServiceMock
            .Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                List<BatchSubtitleItem> batch,
                string _,
                string _,
                List<string>? preContext,
                List<string>? postContext,
                CancellationToken _) =>
            {
                capturedBeforeCounts.Add(preContext?.Count ?? 0);
                capturedAfterCounts.Add(postContext?.Count ?? 0);
                return batch.ToDictionary(item => item.Position, item => $"Przetlumaczona linia {item.Position}");
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);
        var subtitles = Enumerable.Range(1, 30)
            .Select(position => Item(position, $"Subtitle line number {position}"))
            .ToList();

        await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 106,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "deferred",
            batchContextEnabled: true,
            batchContextBefore: 150,
            batchContextAfter: 150,
            cancellationToken: CancellationToken.None);

        Assert.NotEmpty(capturedBeforeCounts);
        Assert.NotEmpty(capturedAfterCounts);
        Assert.All(capturedBeforeCounts, count => Assert.InRange(count, 0, 10));
        Assert.All(capturedAfterCounts, count => Assert.InRange(count, 0, 10));
    }

    private static SubtitleItem Item(int position, string line)
    {
        return new SubtitleItem
        {
            Position = position,
            Lines = [line],
            PlaintextLines = [line]
        };
    }
}
