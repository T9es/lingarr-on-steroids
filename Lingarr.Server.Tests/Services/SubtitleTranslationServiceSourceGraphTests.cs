using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleTranslationServiceSourceGraphTests
{
    [Fact]
    public async Task TranslateSubtitlesBatch_WhenCueIsConservativePassThrough_DoesNotSendItToProviderAndPreservesIt()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
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
                [2] = "Ukryte w sercu uczucia"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["♪～"],
                PlaintextLines = ["♪～"]
            },
            new()
            {
                Position = 2,
                Lines = ["kokoro ni kakushiteta omoi"],
                PlaintextLines = ["kokoro ni kakushiteta omoi"]
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 201,
                Title = "Episode",
                SourceLanguage = "ja",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                Status = TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 50,
            batchRetryMode: "immediate",
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        var sentItem = Assert.Single(sentBatch);
        Assert.Equal(2, sentItem.Position);
        Assert.Equal("kokoro ni kakushiteta omoi", sentItem.Line);
        Assert.Equal(["♪～"], result[0].TranslatedLines);
        Assert.Equal(["Ukryte w sercu uczucia"], result[1].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenDuplicateMemberProviderResultIsMissing_RebuildsFromRepresentative()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
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
            Mock.Of<ILogger>(),
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
                Id = 202,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                Status = TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 50,
            batchRetryMode: "immediate",
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        var sentItem = Assert.Single(sentBatch);
        Assert.Equal(1, sentItem.Position);
        Assert.Equal("{\\an7}Franszczu", result[0].TranslatedLines[0]);
        Assert.Equal("{\\an8}Franszczu", result[1].TranslatedLines[0]);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderOmitsOptionalSemanticNodes_PreservesThemAndDoesNotFail()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
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
                [4] = "Musimy wyjsc natychmiast."
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["[grumbles softly]"],
                PlaintextLines = ["[grumbles softly]"]
            },
            new()
            {
                Position = 2,
                Lines = ["Rent-a-Girlfriend"],
                PlaintextLines = ["Rent-a-Girlfriend"]
            },
            new()
            {
                Position = 3,
                Lines = ["Noho i ka lipo"],
                PlaintextLines = ["Noho i ka lipo"]
            },
            new()
            {
                Position = 4,
                Lines = ["We need to leave right now."],
                PlaintextLines = ["We need to leave right now."]
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 203,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                Status = TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 50,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        Assert.Equal([1, 2, 3, 4], sentBatch.Select(item => item.Position));
        Assert.Equal(["[grumbles softly]"], result[0].TranslatedLines);
        Assert.Equal(["Rent-a-Girlfriend"], result[1].TranslatedLines);
        Assert.Equal(["Noho i ka lipo"], result[2].TranslatedLines);
        Assert.Equal(["Musimy wyjsc natychmiast."], result[3].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenOcrDamagedDialogueExists_SendsItToProvider()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
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
                [40] = "- Nie odpowiadam przed zadnym mezczyzna",
                [291] = "- Przepraszam, Ikki,\nale zgadzam sie z Meelo."
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 40,
                Lines = ["- [ANSWER TO NO MAN"],
                PlaintextLines = ["- [ANSWER TO NO MAN"]
            },
            new()
            {
                Position = 291,
                Lines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."],
                PlaintextLines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."]
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 204,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Show,
                Status = TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: false,
            batchSize: 50,
            batchRetryMode: "immediate",
            cancellationToken: CancellationToken.None);

        var sentBatch = Assert.Single(capturedBatches);
        Assert.Equal([40, 291], sentBatch.Select(item => item.Position));
        Assert.Equal("- [ANSWER TO NO MAN", sentBatch[0].Line);
        Assert.Equal("- I'M SORRY, IKK],\nBUT I'M WITH MEELO ON THIS.", sentBatch[1].Line);
        Assert.Equal(["- Nie odpowiadam przed zadnym mezczyzna"], result[0].TranslatedLines);
        Assert.Equal(["- Przepraszam, Ikki,", "ale zgadzam sie z Meelo."], result[1].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_WhenProviderEchoesSubstantialDialogue_TreatsItAsMissing()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
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
            .ReturnsAsync(new Dictionary<int, string>
            {
                [291] = "- I'M SORRY, IKK],\nBUT I'M WITH MEELO ON THIS."
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            Mock.Of<ILogger>(),
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 291,
                Lines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."],
                PlaintextLines = ["- I'M SORRY, IKK],", "BUT I'M WITH MEELO ON THIS."]
            }
        };

        var exception = await Assert.ThrowsAsync<MissingTranslationException>(() =>
            service.TranslateSubtitlesBatch(
                subtitles,
                new TranslationRequest
                {
                    Id = 205,
                    Title = "Episode",
                    SourceLanguage = "en",
                    TargetLanguage = "pl",
                    MediaType = MediaType.Show,
                    Status = TranslationStatus.Pending
                },
                stripSubtitleFormatting: false,
                preserveAssFormatting: false,
                batchSize: 50,
                batchRetryMode: "immediate",
                cancellationToken: CancellationToken.None));

        Assert.Contains("Translation failed", exception.Message);
        Assert.True(exception.MissingCues.Single().AutoApprovalEligible);
        Assert.DoesNotContain("- I'M SORRY, IKK],", subtitles[0].TranslatedLines);
    }
}
