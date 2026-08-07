using System;
using System.Collections.Generic;
using System.IO;
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
using Microsoft.Extensions.Logging.Abstractions;
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

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitles(
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
        Assert.Contains(
            "echo",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
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

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitles(
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

        Assert.Contains(
            "wrong target language",
            exception.InnerException?.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
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
    public async Task TranslateSubtitlesBatch_WhenRetryingIncompleteCheckpoint_SendsOnlyMissingRepresentativeWithConfiguredContext()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedBatches = new List<List<BatchSubtitleItem>>();
        var capturedPreContexts = new List<List<string>?>();
        var capturedPostContexts = new List<List<string>?>();
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = 111,
            SourceFingerprint = "retry-context",
            Translations = new Dictionary<int, string>
            {
                [1] = "Zdrowa linia przed.",
                [2] = "This line needs a retry.",
                [3] = "Zdrowa linia po."
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
            .Callback((List<BatchSubtitleItem> batch, string _, string _, List<string>? preContext, List<string>? postContext, CancellationToken _) =>
            {
                capturedBatches.Add(batch.ToList());
                capturedPreContexts.Add(preContext?.ToList());
                capturedPostContexts.Add(postContext?.ToList());
            })
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(item => item.Position, _ => "PrzetĹ‚umaczona linia retry."));
        checkpointServiceMock
            .Setup(service => service.LoadAsync(111, "retry-context", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointServiceMock
            .Setup(service => service.SaveCheckpointAsync(
                It.IsAny<TranslationCheckpoint>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        checkpointServiceMock
            .Setup(service => service.SaveTranslationAsync(
                111,
                "retry-context",
                2,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            Item(1, "Previously translated line."),
            Item(2, "This line needs a retry."),
            Item(3, "Following context line.")
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 111,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending,
                SourceSnapshotFingerprint = "retry-context"
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1,
            batchRetryMode: "none",
            batchContextEnabled: false,
            batchContextBefore: 1,
            batchContextAfter: 1,
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        var sentItem = Assert.Single(sentBatch);
        Assert.Equal(2, sentItem.Position);
        Assert.Equal("This line needs a retry.", sentItem.Line);
        Assert.Equal(["Previously translated line."], Assert.Single(capturedPreContexts));
        Assert.Equal(["Following context line."], Assert.Single(capturedPostContexts));
        Assert.DoesNotContain(sentBatch, item => item.Line is "Previously translated line." or "Following context line.");
        Assert.Equal("PrzetĹ‚umaczona linia retry.", result[1].TranslatedLines[0]);
        Assert.Equal("Zdrowa linia przed.", result[0].TranslatedLines[0]);
        Assert.Equal("Zdrowa linia po.", result[2].TranslatedLines[0]);
        checkpointServiceMock.Verify(service => service.SaveTranslationAsync(
            111,
            "retry-context",
            2,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task TranslateSubtitlesBatch_WhenImmediateFallbackSplitsBatch_PreservesConfiguredContext()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var progressServiceMock = new Mock<IProgressService>();
        var capturedCalls = new List<(
            List<int> Positions,
            List<string>? PreContext,
            List<string>? PostContext)>();

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
            .Returns((
                List<BatchSubtitleItem> batch,
                string _,
                string _,
                List<string>? preContext,
                List<string>? postContext,
                CancellationToken _) =>
            {
                capturedCalls.Add((
                    batch.Select(item => item.Position).ToList(),
                    preContext?.ToList(),
                    postContext?.ToList()));

                return batch.Count > 1
                    ? Task.FromException<Dictionary<int, string>>(
                        new TranslationException("Forced full-batch failure."))
                    : Task.FromResult(batch.ToDictionary(
                        item => item.Position,
                        item => $"Przetlumaczona linia {item.Position}"));
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object,
            batchFallbackService: new BatchFallbackService(Mock.Of<ILogger<BatchFallbackService>>()));
        var subtitles = Enumerable.Range(1, 6)
            .Select(position => Item(position, $"Subtitle line {position}"))
            .ToList();

        await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 117,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            batchSize: 2,
            batchRetryMode: "immediate",
            maxSplitAttempts: 2,
            batchContextEnabled: true,
            batchContextBefore: 1,
            batchContextAfter: 1,
            cancellationToken: CancellationToken.None);

        var targetCalls = capturedCalls
            .Where(call => call.Positions.SequenceEqual([3, 4]) ||
                          call.Positions.SequenceEqual([3]) ||
                          call.Positions.SequenceEqual([4]))
            .ToList();
        Assert.Equal(3, targetCalls.Count);
        Assert.All(targetCalls, call =>
        {
            Assert.Equal(["Subtitle line 2"], call.PreContext);
            Assert.Equal(["Subtitle line 5"], call.PostContext);
        });
        Assert.Equal(6, subtitles.Count(subtitle => subtitle.TranslatedLines.Count == 1));
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
    public async Task TranslateSubtitlesBatch_WhenAssSemanticCuesEchoSource_PreservesThemButRejectsOrdinaryDialogue()
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
            new()
            {
                Position = 1,
                Lines = ["{\\an8}SHOW TITLE"],
                PlaintextLines = ["SHOW TITLE"],
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            },
            new()
            {
                Position = 2,
                Lines = ["{\\k20}La la"],
                PlaintextLines = ["La la"],
                SsaDialogue = new SsaDialogue { Style = "Karaoke" }
            },
            Item(3, "This is ordinary dialogue")
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 116,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 3,
            batchRetryMode: "none",
            cancellationToken: CancellationToken.None));

        Assert.Equal([3], exception.MissingCues.Select(cue => cue.Position));
        Assert.Equal("{\\an8}SHOW TITLE", subtitles[0].TranslatedLines[0]);
        Assert.Equal("{\\k20}La la", subtitles[1].TranslatedLines[0]);
        Assert.Empty(subtitles[2].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenSingleResidualDialogueEchoRemains_PreservesSourceWithinTolerance()
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

        // A single residual echo is within the 2%/25 tolerance: the source text is
        // preserved and the request succeeds instead of failing the whole file.
        Assert.Equal(subtitles, result);
        Assert.Equal(["Whoa, whoa, whoa."], subtitles[0].TranslatedLines);
        Assert.Equal("Przetlumaczona linia 2", subtitles[1].TranslatedLines[0]);
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

    [Fact]
    public async Task TranslateSubtitles_WhenSnapshotFingerprintIsPresent_UsesStableSourceContentIdentity()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var progressServiceMock = new Mock<IProgressService>();
        var loadedFingerprints = new List<string>();
        var checkpointTranslations = new Dictionary<string, Dictionary<int, string>>();
        var temporaryDirectory = Directory.CreateTempSubdirectory("lingarr-checkpoint-fingerprint-");
        var sourcePath = Path.Combine(temporaryDirectory.FullName, "source.srt");

        try
        {
            await File.WriteAllTextAsync(sourcePath, "1\n00:00:00,000 --> 00:00:01,000\nHello there\n");

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
                .ReturnsAsync((string text, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                    text == "Hello there"
                        ? "To jest stare tłumaczenie"
                        : "To jest nowe tłumaczenie");
            checkpointServiceMock
                .Setup(service => service.LoadAsync(
                    301,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((int _, string fingerprint, CancellationToken _) =>
                {
                    loadedFingerprints.Add(fingerprint);
                    return checkpointTranslations.TryGetValue(fingerprint, out var translations)
                        ? new TranslationCheckpoint
                        {
                            TranslationRequestId = 301,
                            SourceFingerprint = fingerprint,
                            Translations = new Dictionary<int, string>(translations)
                        }
                        : null;
                });
            checkpointServiceMock
                .Setup(service => service.SaveTranslationAsync(
                    301,
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<int, string, int, string, CancellationToken>(
                    (_, fingerprint, position, translation, _) =>
                    {
                        if (!checkpointTranslations.TryGetValue(fingerprint, out var translations))
                        {
                            translations = new Dictionary<int, string>();
                            checkpointTranslations[fingerprint] = translations;
                        }

                        translations[position] = translation;
                    })
                .Returns(Task.CompletedTask);

            var service = new SubtitleTranslationService(
                translationServiceMock.Object,
                Mock.Of<ILogger>(),
                progressServiceMock.Object,
                checkpointService: checkpointServiceMock.Object);
            var request = new TranslationRequest
            {
                Id = 301,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                SubtitleToTranslate = sourcePath,
                SourceSubtitleFormat = ".srt",
                SourceSnapshotFingerprint = "snapshot-identity",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            };

            var firstResult = await service.TranslateSubtitles(
                [Item(1, "Hello there")],
                request,
                stripSubtitleFormatting: false,
                contextBefore: 0,
                contextAfter: 0,
                preserveAssFormatting: false,
                cancellationToken: CancellationToken.None);
            var retryResult = await service.TranslateSubtitles(
                [Item(1, "Hello there")],
                request,
                stripSubtitleFormatting: false,
                contextBefore: 0,
                contextAfter: 0,
                preserveAssFormatting: false,
                cancellationToken: CancellationToken.None);

            await File.WriteAllTextAsync(sourcePath, "1\n00:00:00,000 --> 00:00:01,000\nGoodbye there\n");
            var replacedSourceResult = await service.TranslateSubtitles(
                [Item(1, "Goodbye there")],
                request,
                stripSubtitleFormatting: false,
                contextBefore: 0,
                contextAfter: 0,
                preserveAssFormatting: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal("To jest stare tłumaczenie", firstResult[0].TranslatedLines[0]);
            Assert.Equal("To jest stare tłumaczenie", retryResult[0].TranslatedLines[0]);
            Assert.Equal("To jest nowe tłumaczenie", replacedSourceResult[0].TranslatedLines[0]);
            Assert.Equal(2, translationServiceMock.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(ITranslationService.TranslateAsync)));
            Assert.Equal(3, loadedFingerprints.Count);
            Assert.Equal(loadedFingerprints[0], loadedFingerprints[1]);
            Assert.NotEqual(loadedFingerprints[0], loadedFingerprints[2]);
            Assert.Contains("content-sha256:", loadedFingerprints[0], StringComparison.Ordinal);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TranslateSubtitles_WhenProviderThrowsGenericFailure_ReturnsMissingTranslationData()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TranslationException("Provider rejected the translation request."));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            Mock.Of<IProgressService>());

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitles(
            [Item(501, "A line that must remain reviewable")],
            new TranslationRequest
            {
                Id = 501,
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

        var cue = Assert.Single(exception.MissingCues);
        Assert.Equal(501, cue.Position);
        Assert.Equal("A line that must remain reviewable", cue.SourceText);
        Assert.Contains("Provider rejected", exception.InnerException?.InnerException?.Message ?? string.Empty);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenCheckpointMarksSourcePreservedPositions_HydratesThemWithoutProviderRequests()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var checkpointServiceMock = new Mock<ITranslationCheckpointService>();
        var progressServiceMock = new Mock<IProgressService>();
        List<BatchSubtitleItem>? capturedBatch = null;

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
                capturedBatch = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [4] = "Przetłumaczona zwykła linia"
            });
        checkpointServiceMock
            .Setup(service => service.LoadAsync(401, "source-preserved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranslationCheckpoint
            {
                TranslationRequestId = 401,
                SourceFingerprint = "source-preserved",
                Translations = new Dictionary<int, string>
                {
                    [1] = "Opening line one",
                    [2] = "Opening line two",
                    [3] = "Opening line three"
                },
                SourcePreservedPositions = [1, 2, 3]
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object,
            checkpointService: checkpointServiceMock.Object);
        var subtitles =
            new[]
            {
                Item(1, "Opening line one"),
                Item(2, "Opening line two"),
                Item(3, "Opening line three"),
                Item(4, "Ordinary dialogue")
            }
            .ToList();

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 401,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                SourceSnapshotFingerprint = "source-preserved",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedBatch);
        Assert.Equal([4], capturedBatch!.Select(item => item.Position));
        Assert.Equal("Opening line one", result[0].TranslatedLines[0]);
        Assert.Equal("Opening line two", result[1].TranslatedLines[0]);
        Assert.Equal("Opening line three", result[2].TranslatedLines[0]);
        Assert.Equal("Przetłumaczona zwykła linia", result[3].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenSameTextHasDialogueAndSignSemantics_DoesNotShareProviderRepresentative()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var progressServiceMock = new Mock<IProgressService>();
        List<BatchSubtitleItem>? capturedBatch = null;

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
                capturedBatch = batch;
            })
            .ReturnsAsync((List<BatchSubtitleItem> batch, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                batch.ToDictionary(
                    item => item.Position,
                    item => item.Position == 1 ? "Dialog tłumaczenie" : "Znak tłumaczenie"));

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);
        var subtitles = new List<SubtitleItem>
        {
            Item(1, "ON SCREEN"),
            new()
            {
                Position = 2,
                Lines = ["ON SCREEN"],
                PlaintextLines = ["ON SCREEN"],
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 402,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedBatch);
        Assert.Equal([1, 2], capturedBatch!.Select(item => item.Position));
        Assert.Equal("Dialog tłumaczenie", result[0].TranslatedLines[0]);
        Assert.Equal("Znak tłumaczenie", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenSnapshotIdentityChangesWithSameSourceBytes_DoesNotReuseSourcePreservedCheckpoint()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var progressServiceMock = new Mock<IProgressService>();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source.srt");
        var providerCallCount = 0;

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            sourcePath,
            "1\n00:00:00,000 --> 00:00:01,000\nOpening line\n");

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
            .ReturnsAsync((
                string _,
                string _,
                string _,
                List<string>? _,
                List<string>? _,
                CancellationToken _) =>
            {
                var call = Interlocked.Increment(ref providerCallCount);
                return call == 1 ? "Translated version one" : "Translated version two";
            });

        try
        {
            var checkpointService = new TranslationCheckpointService(
                NullLogger<TranslationCheckpointService>.Instance,
                root);
            var service = new SubtitleTranslationService(
                translationServiceMock.Object,
                Mock.Of<ILogger>(),
                progressServiceMock.Object,
                checkpointService: checkpointService);
            var firstRequest = new TranslationRequest
            {
                Id = 701,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                SubtitleToTranslate = sourcePath,
                SourceSubtitleFormat = ".srt",
                SourceSnapshotType = "embedded",
                SourceSnapshotIdentity = "embedded|en|stream:1",
                SourceSnapshotFingerprint = "snapshot-same-revision",
                SourceSnapshotStreamIndex = 1,
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            };

            await service.TranslateSubtitles(
                [Item(1, "Opening line")],
                firstRequest,
                stripSubtitleFormatting: false,
                contextBefore: 0,
                contextAfter: 0,
                preserveAssFormatting: false,
                cancellationToken: CancellationToken.None);

            var cachedCheckpoint = await checkpointService.LoadByRequestIdAsync(
                firstRequest.Id,
                CancellationToken.None);
            Assert.NotNull(cachedCheckpoint);
            var firstFingerprint = cachedCheckpoint!.SourceFingerprint;
            cachedCheckpoint!.Translations[1] = "Opening line";
            cachedCheckpoint.SourcePreservedPositions.Add(1);
            await checkpointService.SaveCheckpointAsync(cachedCheckpoint, CancellationToken.None);

            var secondRequest = new TranslationRequest
            {
                Id = firstRequest.Id,
                Title = firstRequest.Title,
                SourceLanguage = firstRequest.SourceLanguage,
                TargetLanguage = firstRequest.TargetLanguage,
                SubtitleToTranslate = firstRequest.SubtitleToTranslate,
                SourceSubtitleFormat = firstRequest.SourceSubtitleFormat,
                SourceSnapshotType = "embedded",
                SourceSnapshotIdentity = "embedded|en|stream:2",
                SourceSnapshotFingerprint = "snapshot-same-revision",
                SourceSnapshotStreamIndex = 2,
                MediaType = firstRequest.MediaType,
                Status = firstRequest.Status
            };

            var result = await service.TranslateSubtitles(
                [Item(1, "Opening line")],
                secondRequest,
                stripSubtitleFormatting: false,
                contextBefore: 0,
                contextAfter: 0,
                preserveAssFormatting: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, providerCallCount);
            Assert.Equal("Translated version two", result[0].TranslatedLines[0]);
            var refreshedCheckpoint = await checkpointService.LoadByRequestIdAsync(
                secondRequest.Id,
                CancellationToken.None);
            Assert.NotNull(refreshedCheckpoint);
            Assert.NotEqual(firstFingerprint, refreshedCheckpoint!.SourceFingerprint);
            Assert.Empty(refreshedCheckpoint.SourcePreservedPositions);
            Assert.Equal("Translated version two", refreshedCheckpoint.Translations[1]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TranslateSubtitles_WhenExactSemanticDuplicatesExist_UsesOneProviderRequestAndPreservesEachAssStructure()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var progressServiceMock = new Mock<IProgressService>();
        var calls = new List<(string Text, List<string>? Before, List<string>? After)>();

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
            .Callback<string, string, string, List<string>?, List<string>?, CancellationToken>(
                (text, _, _, before, after, _) => calls.Add((text, before, after)))
            .ReturnsAsync("Translated duplicate");

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);
        var subtitles = new List<SubtitleItem>
        {
            Item(1, "♪"),
            new()
            {
                Position = 2,
                Lines = ["{\\an7}Repeated line"],
                PlaintextLines = ["Repeated line"]
            },
            new()
            {
                Position = 3,
                Lines = ["{\\an8}Repeated line"],
                PlaintextLines = ["Repeated line"]
            },
            Item(4, "♪")
        };

        var result = await service.TranslateSubtitles(
            subtitles,
            new TranslationRequest
            {
                Id = 702,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            contextBefore: 1,
            contextAfter: 1,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        var call = Assert.Single(calls);
        Assert.Equal("Repeated line", call.Text);
        Assert.Equal(["♪"], call.Before);
        Assert.Equal(["Repeated line"], call.After);
        Assert.Equal("{\\an7}Translated duplicate", result[1].TranslatedLines[0]);
        Assert.Equal("{\\an8}Translated duplicate", result[2].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitles_WhenSameTextHasDifferentSemanticKinds_UsesSeparateProviderRepresentatives()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var progressServiceMock = new Mock<IProgressService>();
        var providerCallCount = 0;

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
            .ReturnsAsync((
                string _,
                string _,
                string _,
                List<string>? _,
                List<string>? _,
                CancellationToken _) =>
            {
                var call = Interlocked.Increment(ref providerCallCount);
                return call == 1 ? "Translated dialogue" : "Translated sign";
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);
        var result = await service.TranslateSubtitles(
            [
                Item(1, "Repeated line"),
                new SubtitleItem
                {
                    Position = 2,
                    Lines = ["Repeated line"],
                    PlaintextLines = ["Repeated line"],
                    SsaDialogue = new SsaDialogue { Style = "Signs" }
                }
            ],
            new TranslationRequest
            {
                Id = 703,
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
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, providerCallCount);
        Assert.Equal("Translated dialogue", result[0].TranslatedLines[0]);
        Assert.Equal("Translated sign", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderOmitsCueRepeatedThreeTimes_PreservesChantFromSource()
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
                batch
                    .Where(item => item.Line != "We did it again!")
                    .ToDictionary(item => item.Position, item => $"Przetlumaczona linia {item.Position}"));

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
                FailedPositions = failedItems.Select(item => item.Position).ToHashSet(),
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
            Item(1, "We need to get out of here."),
            Item(2, "We did it again!"),
            Item(3, "Follow the plan."),
            Item(4, "We did it again!"),
            Item(5, "We did it again!")
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 801,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 5,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        // The chant appears 3+ times, so it is treated as a refrain and preserved
        // from source when the provider omits it instead of failing the whole file.
        Assert.Equal(subtitles, result);
        Assert.Equal("Przetlumaczona linia 1", result[0].TranslatedLines[0]);
        Assert.Equal(["We did it again!"], result[1].TranslatedLines);
        Assert.Equal("Przetlumaczona linia 3", result[2].TranslatedLines[0]);
        Assert.Equal(["We did it again!"], result[3].TranslatedLines);
        Assert.Equal(["We did it again!"], result[4].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderOmitsCueRepeatedOnlyTwice_StillFails()
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
                batch
                    .Where(item => item.Line != "We did it again!")
                    .ToDictionary(item => item.Position, item => $"Przetlumaczona linia {item.Position}"));

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
                FailedPositions = failedItems.Select(item => item.Position).ToHashSet(),
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
            Item(1, "We need to get out of here."),
            Item(2, "Follow the plan."),
            Item(3, "We did it again!"),
            Item(4, "We did it again!")
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 802,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 4,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None));

        // Two occurrences are below the repeat threshold: the cue stays ordinary
        // dialogue and is not auto-filled from source.
        Assert.Equal([3, 4], exception.MissingCues.Select(cue => cue.Position));
        Assert.All(exception.MissingCues, cue => Assert.False(cue.AutoApprovalEligible));
        Assert.Empty(subtitles[2].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderOmitsTinyArtifactCue_CompletesWithSourceFill()
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
                batch
                    .Where(item => item.Line != "#4")
                    .ToDictionary(item => item.Position, item => $"Przetlumaczona linia {item.Position}"));

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
                FailedPositions = failedItems.Select(item => item.Position).ToHashSet(),
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
            Item(1, "#4"),
            Item(2, "A normal dialogue line"),
            Item(3, "Another normal line")
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 803,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 3,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        // A 2-char artifact cue omitted by the provider is within the residual
        // tolerance and is preserved from source instead of failing the file.
        Assert.Equal(subtitles, result);
        Assert.Equal(["#4"], result[0].TranslatedLines);
        Assert.Equal("Przetlumaczona linia 2", result[1].TranslatedLines[0]);
        Assert.Equal("Przetlumaczona linia 3", result[2].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenMoreShortCuesAreMissingThanToleranceBudget_StillFails()
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
                batch
                    .Where(item => !item.Line.StartsWith("#", StringComparison.Ordinal))
                    .ToDictionary(item => item.Position, item => $"Przetlumaczona linia {item.Position}"));

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
                FailedPositions = failedItems.Select(item => item.Position).ToHashSet(),
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

        // 1300 translatable cues give a residual tolerance of 25 (2% ratio capped at 25).
        var subtitles = Enumerable.Range(1, 1300)
            .Select(position => Item(position, position <= 30 ? $"#{position}" : $"Subtitle line {position}"))
            .ToList();

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() => service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 804,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 1300,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None));

        // 30 short cues are missing: 25 fit in the shared residual budget and are
        // preserved from source, the remaining 5 still fail the request.
        Assert.Equal([26, 27, 28, 29, 30], exception.MissingCues.Select(cue => cue.Position));
        Assert.Equal(["#1"], subtitles[0].TranslatedLines);
        Assert.Equal(["#25"], subtitles[24].TranslatedLines);
        Assert.Empty(subtitles[25].TranslatedLines);
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
