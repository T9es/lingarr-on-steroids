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
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleTextStructureTests
{
    [Fact]
    public void AssPrefixTag_ShouldExposeVisibleTextAndRestoreTagOnApply()
    {
        var sourceLines = new List<string> { "{\\an8}Hello world" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal("Hello world", structure.ProviderVisibleText);

        var translated = structure.ApplyProviderTranslation("Witaj swiecie");
        Assert.Single(translated);
        Assert.Equal("{\\an8}Witaj swiecie", translated[0]);
    }

    [Fact]
    public void AssHardBreak_ShouldMapProviderNewlinesToAssBreaks()
    {
        var sourceLines = new List<string> { "Line one\\NLine two" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal("Line one\nLine two", structure.ProviderVisibleText);
        Assert.True(structure.IsProviderTranslationCompatible("Pierwsza linia\nDruga linia"));

        var translated = structure.ApplyProviderTranslation("Pierwsza linia\nDruga linia");
        Assert.Single(translated);
        Assert.Equal("Pierwsza linia\\NDruga linia", translated[0]);
    }

    [Fact]
    public void AssHardBreak_WhenProviderCollapsesLines_ShouldReflowIntoOriginalBreaks()
    {
        var sourceLines = new List<string> { "Line one\\NLine two" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Pierwsza linia Druga linia");

        Assert.Single(translated);
        Assert.Equal("Pierwsza linia\\NDruga linia", translated[0]);
    }

    [Fact]
    public void InlineMarkup_WhenProviderCollapsesSrtLines_ShouldReflowIntoOriginalLineCount()
    {
        var sourceLines = new List<string>
        {
            "<i>Line one</i>",
            "Line two"
        };
        var structure = BuildInlineStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Pierwsza linia Druga linia");

        Assert.Equal(2, translated.Count);
        Assert.Equal("<i>Pierwsza linia</i>", translated[0]);
        Assert.Equal("Druga linia", translated[1]);
    }

    [Fact]
    public void AssHardBreak_WhenProviderReturnsTooManyLines_ShouldReflowIntoOriginalBreaks()
    {
        var sourceLines = new List<string> { "Line one\\NLine two" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Pierwsza\nlinia\nDruga linia");

        Assert.Single(translated);
        Assert.Equal("Pierwsza linia\\NDruga linia", translated[0]);
    }

    [Fact]
    public void AssHardBreak_WhenTargetLanguageIsLonger_ShouldPreferPunctuationAndWordBoundaries()
    {
        var sourceLines = new List<string> { "Stop here\\NDo not enter" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Zatrzymaj sie tutaj, prosze nie wchodzic dalej");

        Assert.Single(translated);
        Assert.Equal("Zatrzymaj sie tutaj,\\Nprosze nie wchodzic dalej", translated[0]);
    }

    [Fact]
    public void AssHardBreak_WhenProviderReturnsNoSpaceCjkText_ShouldSplitOnTextElementBoundary()
    {
        var sourceLines = new List<string> { "First line\\NSecond line" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("这是一个很长的中文字幕需要分成两行");

        Assert.Single(translated);
        Assert.Contains("\\N", translated[0], StringComparison.Ordinal);
        var lines = translated[0].Split("\\N", StringSplitOptions.None);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        Assert.Equal("这是一个很长的中文字幕需要分成两行", string.Concat(lines));
    }

    [Fact]
    public void AssHardBreak_WhenProviderReturnsEmojiText_ShouldNotSplitInsideGraphemeCluster()
    {
        var sourceLines = new List<string> { "First line\\NSecond line" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Alert 👨‍👩‍👧‍👦 family sign ahead");

        Assert.Single(translated);
        Assert.Contains("\\N", translated[0], StringComparison.Ordinal);
        Assert.Contains("👨‍👩‍👧‍👦", translated[0], StringComparison.Ordinal);
        Assert.DoesNotContain("👨\\N", translated[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\\N‍", translated[0], StringComparison.Ordinal);
    }

    [Fact]
    public void InlineMarkup_WhenProviderReturnsLongToken_ShouldKeepTokenIntactWhenBoundaryExists()
    {
        var sourceLines = new List<string>
        {
            "<i>Visit now</i>",
            "Then continue"
        };
        var structure = BuildInlineStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Odwiedz https://example.com/very/long/path teraz kontynuuj dalej");

        Assert.Equal(2, translated.Count);
        Assert.Contains(translated, line => line.Contains("https://example.com/very/long/path", StringComparison.Ordinal));
        Assert.DoesNotContain(translated, line => line.Contains("https://example.", StringComparison.Ordinal) &&
            !line.Contains("https://example.com/very/long/path", StringComparison.Ordinal));
    }

    [Fact]
    public void InlineMarkup_WhenProviderLineCountMatches_ShouldKeepProviderLinesUnchanged()
    {
        var sourceLines = new List<string>
        {
            "<i>Line one</i>",
            "Line two"
        };
        var structure = BuildInlineStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Short\nA much longer translated second line");

        Assert.Equal(2, translated.Count);
        Assert.Equal("<i>Short</i>", translated[0]);
        Assert.Equal("A much longer translated second line", translated[1]);
    }

    [Fact]
    public void AssDrawingOnly_ShouldNotExposeProviderText()
    {
        var sourceLines = new List<string> { "{\\p1}m 0 0 l 10 10{\\p0}" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal(0, structure.VisibleLineCount);
        Assert.Equal(string.Empty, structure.ProviderVisibleText);

        var translated = structure.ApplyProviderTranslation("ignored");
        Assert.Single(translated);
        Assert.Equal("{\\p1}m 0 0 l 10 10{\\p0}", translated[0]);
    }

    [Fact]
    public void AssMixedDrawingAndText_ShouldHideDrawingFromProviderAndPreserveLocally()
    {
        var sourceLines = new List<string> { "{\\p1}m 0 0 l 10 10{\\p0}{\\an8}Hello" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal("Hello", structure.ProviderVisibleText);
        Assert.DoesNotContain("m 0 0", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("{", structure.ProviderVisibleText, StringComparison.Ordinal);

        var translated = structure.ApplyProviderTranslation("Czesc");
        Assert.Single(translated);
        Assert.Equal("{\\p1}m 0 0 l 10 10{\\p0}{\\an8}Czesc", translated[0]);
    }

    [Fact]
    public void AssKaraoke_ShouldPreserveKaraokeTagsLocally()
    {
        var sourceLines = new List<string> { "{\\k20}He{\\k20}llo" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal("Hello", structure.ProviderVisibleText);

        var translated = structure.ApplyProviderTranslation("Witaj");
        Assert.Single(translated);
        Assert.Contains("{\\k20}", translated[0], StringComparison.Ordinal);
        Assert.Equal("{\\k20}Wi{\\k20}taj", translated[0]);
    }

    [Fact]
    public void AssSingleVisibleTextOverlay_ShouldKeepExactTextAndPlaceInlineTagsAroundMatchingSourcePhrase()
    {
        var sourceLines = new List<string> { "Je dis au revoir, {\\i0}say goodbye{\\i1}" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslationAsSingleVisibleText("Mowie do widzenia,say goodbye");

        Assert.Single(translated);
        Assert.Equal("Mowie do widzenia,{\\i0}say goodbye{\\i1}", translated[0]);
    }

    [Fact]
    public void AssSingleVisibleTextOverlay_ShouldAvoidDanglingInlineTagsWhenSourcePhraseIsNotPresent()
    {
        var sourceLines = new List<string> { "Je dis au revoir, {\\i0}phrase introuvable{\\i1}" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslationAsSingleVisibleText("Mowie do widzenia");

        Assert.Single(translated);
        Assert.Equal("Mowie do widzenia", translated[0]);
    }

    [Fact]
    public void AssSingleVisibleTextOverlay_ShouldPreserveWholeCueWrapperTags()
    {
        var sourceLines = new List<string> { "{\\i1}Bonjour{\\i}" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslationAsSingleVisibleText("Czesc");

        Assert.Single(translated);
        Assert.Equal("{\\i1}Czesc{\\i}", translated[0]);
    }

    [Fact]
    public void AssInlineFormatting_ShouldKeepProviderTextIntactAndAnchorTagsAroundMatchingPhrase()
    {
        var sourceLines = new List<string> { "Can I go to your house to play {\\i1}Brave Star{\\i0}?" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Moge wpasc do ciebie pograc w Brave Star?");

        Assert.Single(translated);
        Assert.Equal("Moge wpasc do ciebie pograc w {\\i1}Brave Star{\\i0}?", translated[0]);
    }

    [Fact]
    public void AssInlineFormatting_ShouldDropUnmatchedPhraseTagsInsteadOfSplittingProviderText()
    {
        var sourceLines = new List<string> { "Can I go to your house to play {\\i1}Brave Star{\\i0}?" };
        var structure = BuildAssStructure(sourceLines);

        var translated = structure.ApplyProviderTranslation("Moge wpasc do ciebie pograc w Gwiezdna Odwage?");

        Assert.Single(translated);
        Assert.Equal("Moge wpasc do ciebie pograc w Gwiezdna Odwage?", translated[0]);
    }

    [Fact]
    public void SrtInlineHtml_ShouldProtectMarkupFromProviderAndRestoreAfterTranslation()
    {
        var sourceLines = new List<string> { "<i>Hello</i> <b>world</b>" };
        var structure = BuildInlineStructure(sourceLines);

        Assert.Equal("Hello world", structure.ProviderVisibleText);
        Assert.DoesNotContain("<", structure.ProviderVisibleText, StringComparison.Ordinal);

        var translated = structure.ApplyProviderTranslation("Czesc swiecie");
        Assert.Single(translated);
        Assert.Equal("<i>Czesc</i> <b>swiecie</b>", translated[0]);
    }

    [Fact]
    public void VttCueSpans_ShouldProtectCueMarkupFromProviderAndRestoreAfterTranslation()
    {
        var sourceLines = new List<string> { "<v Speaker><lang en>Hello</lang></v>" };
        var structure = BuildInlineStructure(sourceLines);

        Assert.Equal("Hello", structure.ProviderVisibleText);
        Assert.DoesNotContain("<", structure.ProviderVisibleText, StringComparison.Ordinal);

        var translated = structure.ApplyProviderTranslation("Czesc");
        Assert.Single(translated);
        Assert.Equal("<v Speaker><lang en>Czesc</lang></v>", translated[0]);
    }

    [Fact]
    public void InlineMarkup_ShouldKeepUnknownAngleBracketTextVisible()
    {
        var sourceLines = new List<string> { "Math says <foo>2 < 3</foo> and <i>hello</i>" };
        var structure = BuildInlineStructure(sourceLines);

        Assert.Contains("<foo>", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.Contains("</foo>", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.Contains("< 3", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>", structure.ProviderVisibleText, StringComparison.Ordinal);
    }

    [Fact]
    public void AssInlineMarkup_ShouldProtectInlineAndAssTagsFromProvider()
    {
        var sourceLines = new List<string> { "<font color=\"#fff\">{\\an7}Hello</font>" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Equal("Hello", structure.ProviderVisibleText);
        Assert.DoesNotContain("<font", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\an7}", structure.ProviderVisibleText, StringComparison.Ordinal);

        var translated = structure.ApplyProviderTranslation("Czesc");
        Assert.Single(translated);
        Assert.Equal("<font color=\"#fff\">{\\an7}Czesc</font>", translated[0]);
    }

    [Fact]
    public void AssInlineMarkup_ShouldKeepUnknownAngleBracketTextVisible()
    {
        var sourceLines = new List<string> { "{\\an8}Math says <foo>2 < 3</foo>" };
        var structure = BuildAssStructure(sourceLines);

        Assert.Contains("<foo>", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.Contains("</foo>", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.Contains("< 3", structure.ProviderVisibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\an8}", structure.ProviderVisibleText, StringComparison.Ordinal);
    }

    [Fact]
    public void VttTimestampTag_ShouldBeProtected()
    {
        var sourceLines = new List<string> { "Go <00:01:02.300> now" };
        var structure = BuildInlineStructure(sourceLines);

        Assert.Equal("Go  now", structure.ProviderVisibleText);
        var translated = structure.ApplyProviderTranslation("Start now");
        Assert.Single(translated);
        Assert.Equal("Start<00:01:02.300>now", translated[0]);
    }

    [Fact]
    public async Task BatchFlow_ShouldSendOneVisibleItemPerCueWithoutRawMarkup()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = new Mock<IBatchTranslationService>();
        var loggerMock = new Mock<ILogger>();
        List<BatchSubtitleItem>? capturedBatch = null;

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
                [1] = "Pierwsza",
                [2] = "Druga"
            });

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            Mock.Of<IProgressService>());

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an8}Hello"],
                PlaintextLines = ["Hello"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            },
            new()
            {
                Position = 2,
                Lines = ["<i>World</i>"],
                PlaintextLines = ["World"]
            }
        };

        await service.ProcessSubtitleBatch(
            subtitles,
            batchServiceMock.Object,
            "en",
            "pl",
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedBatch);
        Assert.Equal(2, capturedBatch!.Count);
        Assert.Equal(1, capturedBatch[0].Position);
        Assert.Equal(2, capturedBatch[1].Position);
        Assert.Equal("Hello", capturedBatch[0].Line);
        Assert.Equal("World", capturedBatch[1].Line);
        Assert.DoesNotContain("{", capturedBatch[0].Line, StringComparison.Ordinal);
        Assert.DoesNotContain("<", capturedBatch[1].Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleFlow_ShouldUseVisibleTextAndReconstructAssOutput()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        string? capturedInput = null;

        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((string input, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedInput = input;
            })
            .ReturnsAsync("Przetlumaczony napis");

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an8}A sign"],
                PlaintextLines = ["A sign"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        await service.TranslateSubtitles(
            subtitles,
            new TranslationRequest
            {
                Id = 1,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            contextBefore: 0,
            contextAfter: 0,
            preserveAssFormatting: true,
            cancellationToken: CancellationToken.None);

        Assert.Equal("A sign", capturedInput);
        Assert.Single(subtitles[0].TranslatedLines);
        Assert.Equal("{\\an8}Przetlumaczony napis", subtitles[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task SingleFlow_WhenPlainSubtitleContainsAssSyntax_ShouldUseVisibleTextAndReconstructTags()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        string? capturedInput = null;

        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((string input, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
            {
                capturedInput = input;
            })
            .ReturnsAsync("Czesc");

        progressServiceMock
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var service = new SubtitleTranslationService(
            translationServiceMock.Object,
            loggerMock.Object,
            progressServiceMock.Object);

        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 1,
                Lines = ["{\\an8}Hello"],
                PlaintextLines = ["Hello"]
            }
        };

        await service.TranslateSubtitles(
            subtitles,
            new TranslationRequest
            {
                Id = 1,
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

        Assert.Equal("Hello", capturedInput);
        Assert.Single(subtitles[0].TranslatedLines);
        Assert.Equal("{\\an8}Czesc", subtitles[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task BatchFlow_WhenProviderCollapsesMultilineCue_ShouldReflowWithoutDeferredRepair()
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
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Pierwsza linia Druga linia"
            });

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
                Lines = ["{\\an8}Line one\\NLine two"],
                PlaintextLines = ["Line one Line two"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 2,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 10,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.Single(result[0].TranslatedLines);
        Assert.Equal("{\\an8}Pierwsza linia\\NDruga linia", result[0].TranslatedLines[0]);
        batchFallbackMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeferredRepair_ShouldRecoverLineMismatchWhenRepairReturnsCollapsedTranslation()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var batchFallbackMock = new Mock<IBatchFallbackService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairService = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        List<BatchSubtitleItem>? capturedRepairBatchItems = null;

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

        batchFallbackMock
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
                capturedRepairBatchItems = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Pierwsza linia Druga linia"
            });

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
                Lines = ["{\\an8}Line one\\NLine two"],
                PlaintextLines = ["Line one Line two"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        var result = await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 2,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 10,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedRepairBatchItems);
        var failedRepairItem = Assert.Single(capturedRepairBatchItems!, item => item.Position == 1);
        Assert.Equal("Line one\nLine two", failedRepairItem.Line);
        Assert.Single(result[0].TranslatedLines);
        Assert.Equal("{\\an8}Pierwsza linia\\NDruga linia", result[0].TranslatedLines[0]);
    }

    [Fact]
    public async Task DeferredRepair_WhenRepairReturnsCollapsedTranslation_ShouldNotLogServiceSuccessBeforeCallerAppliesIt()
    {
        var translationServiceMock = new Mock<ITranslationService>();
        var batchServiceMock = translationServiceMock.As<IBatchTranslationService>();
        var batchFallbackMock = new Mock<IBatchFallbackService>();
        var loggerMock = new Mock<ILogger>();
        var progressServiceMock = new Mock<IProgressService>();
        var deferredRepairService = new DeferredRepairService(Mock.Of<ILogger<DeferredRepairService>>());
        List<BatchSubtitleItem>? capturedRepairBatchItems = null;

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

        batchFallbackMock
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
                capturedRepairBatchItems = batch;
            })
            .ReturnsAsync(new Dictionary<int, string>
            {
                [1] = "Pierwsza linia Druga linia"
            });

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
                Lines = ["{\\an8}Line one\\NLine two"],
                PlaintextLines = ["Line one Line two"],
                SsaFormat = new SsaFormat { WrapStyle = SsaWrapStyle.None },
                SsaDialogue = new SsaDialogue { Style = "Signs" }
            }
        };

        await service.TranslateSubtitlesBatch(
            subtitles,
            new TranslationRequest
            {
                Id = 3,
                Title = "Episode",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = Lingarr.Core.Enum.MediaType.Show,
                Status = Lingarr.Core.Enum.TranslationStatus.Pending
            },
            stripSubtitleFormatting: false,
            preserveAssFormatting: true,
            batchSize: 10,
            batchRetryMode: "deferred",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(capturedRepairBatchItems);
        var failedRepairItem = Assert.Single(capturedRepairBatchItems!, item => item.Position == 1);
        Assert.Equal("Line one\nLine two", failedRepairItem.Line);
        loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Deferred repair succeeded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private static SubtitleTextStructure BuildAssStructure(IReadOnlyList<string> sourceLines)
    {
        var parser = new AssTextStructureParser();
        var parsedLines = parser.Parse(sourceLines);
        return new SubtitleTextStructure(SubtitleStructureMode.Ass, sourceLines, parsedLines);
    }

    private static SubtitleTextStructure BuildInlineStructure(IReadOnlyList<string> sourceLines)
    {
        var parser = new InlineMarkupStructureParser();
        var parsedLines = parser.Parse(sourceLines);
        return new SubtitleTextStructure(SubtitleStructureMode.InlineMarkup, sourceLines, parsedLines);
    }
}
