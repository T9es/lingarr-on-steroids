using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleOutputReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileLibraryOutputsAsync_DeletesDbKnownObsoleteSrtWhenToggleOff()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var srtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(assPath, "ass");
        await File.WriteAllTextAsync(srtPath, "srt");
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass,.srt",
            assPath,
            JsonSerializer.Serialize(new[] { assPath, srtPath }));
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "match-source",
            subtitles: [],
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(1, result.DeletedFiles);
        Assert.False(File.Exists(srtPath));
        Assert.True(File.Exists(assPath));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_PreservesUntaggedUserSrtWhenToggleOff()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var userSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.srt");
        await File.WriteAllTextAsync(assPath, "ass");
        await File.WriteAllTextAsync(userSrtPath, "user");
        AddCompletedRequest(context, movie.Id, ".ass", ".ass,.srt", assPath);
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "match-source",
            subtitles:
            [
                new Subtitles
                {
                    Path = userSrtPath,
                    FileName = "movie.pl",
                    Language = "pl",
                    Format = ".srt"
                }
            ],
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, result.SkippedUnsafeFiles);
        Assert.True(File.Exists(userSrtPath));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_QueuesMissingOutputsWhenToggleOn()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        AddMovie(context, tempDirectory.Path);
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "both",
            subtitles: [],
            queuedTranslations: 1);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(1, result.QueuedTranslations);
        Assert.Equal(1, result.QueuedForRetranslation);
    }

    [Fact]
    public async Task ReconcileMediaOutputsAsync_OnlyProcessesRequestedMovie()
    {
        await using var context = BuildContext();
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        var firstMovie = AddMovie(context, firstDirectory.Path);
        AddMovie(context, secondDirectory.Path);
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "both",
            subtitles: [],
            queuedTranslations: 1);

        var result = await service.ReconcileMediaOutputsAsync(firstMovie.Id, MediaType.Movie);

        Assert.Equal(1, result.MediaItemsScanned);
        Assert.Equal(1, result.QueuedTranslations);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfillsMissingSrtFromTranslatedAssWithoutQueueing()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedAssPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\an7}Hello"));
        await File.WriteAllTextAsync(translatedAssPath, BuildAssContent("{\\an7}Czesc"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass",
            translatedAssPath,
            JsonSerializer.Serialize(new[] { translatedAssPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1);

        var result = await service.ReconcileLibraryOutputsAsync();
        var srtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        var srt = await File.ReadAllTextAsync(srtPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.True(File.Exists(srtPath));
        Assert.Contains("Czesc", srt);
        Assert.DoesNotContain(@"{\an7}", srt);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfillsMissingKnownSrtEvenWhenLegacyManagedSrtExists()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedAssPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var expectedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        var legacySrtPath = Path.Combine(tempDirectory.Path, "movie.en.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\an7}Hello"));
        await File.WriteAllTextAsync(translatedAssPath, BuildAssContent("{\\an7}Czesc"));
        await File.WriteAllTextAsync(legacySrtPath, BuildSrtContent("Legacy text"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass,.srt",
            translatedAssPath,
            JsonSerializer.Serialize(new[] { translatedAssPath, expectedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();
        var expectedSrt = await File.ReadAllTextAsync(expectedSrtPath);
        var legacySrt = await File.ReadAllTextAsync(legacySrtPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.True(File.Exists(expectedSrtPath));
        Assert.Contains("Czesc", expectedSrt);
        Assert.Contains("Legacy text", legacySrt);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfillsMissingAssFromSourceAssAndTranslatedSrtWhenCountsMatch()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\an7}Hello"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var ass = await File.ReadAllTextAsync(assPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.Contains("Dialogue: Marked=0,0:00:01.00,0:00:03.00,Sign", ass);
        Assert.Contains(@"{\an7}Czesc", ass);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfillsMissingKnownAssEvenWhenLegacyManagedAssExists()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        var expectedAssPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var legacyAssPath = Path.Combine(tempDirectory.Path, "movie.en.pl.lingarr.ass");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\an7}Hello"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        await File.WriteAllTextAsync(legacyAssPath, BuildAssContent("Legacy text"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass,.srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { expectedAssPath, translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();
        var expectedAss = await File.ReadAllTextAsync(expectedAssPath);
        var legacyAss = await File.ReadAllTextAsync(legacyAssPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.True(File.Exists(expectedAssPath));
        Assert.Contains(@"{\an7}Czesc", expectedAss);
        Assert.Contains("Legacy text", legacyAss);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_PreservesAssTimingStylesAndTagsWhenBackfillingAss()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\pos(10,20)}Hello", style: "Caption"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Witaj"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        await service.ReconcileLibraryOutputsAsync();
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var ass = await File.ReadAllTextAsync(assPath);

        Assert.Contains("Style: Caption,Arial,20", ass);
        Assert.Contains("0:00:01.00,0:00:03.00,Caption", ass);
        Assert.Contains(@"{\pos(10,20)}Witaj", ass);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfilledAssKeepsSrtTextIntactAroundInlineOverrideTags()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent(@"Je dis au revoir, {\i0}say goodbye{\i1}"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Mowie do widzenia,say goodbye"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var ass = await File.ReadAllTextAsync(assPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.Contains(@"Mowie do widzenia,{\i0}say goodbye{\i1}", ass);
        Assert.DoesNotContain(@"say{\i0}goodbye", ass);
    }

    [Fact]
    public async Task RepairExistingAssOutputsAsync_RebuildsInlineTagSpacingFromTranslatedAss()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedAssPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent(@"Play {\i1}Brave Star{\i0}?"));
        await File.WriteAllTextAsync(translatedAssPath, BuildAssContent(@"Gram w{\i1}Brave{\i0}Star?"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Gram wBraveStar?"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass,.srt",
            translatedAssPath,
            JsonSerializer.Serialize(new[] { translatedAssPath, translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();
        var request = await context.TranslationRequests.SingleAsync();
        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var backfillService = new SubtitleOutputBackfillService(
            context,
            subtitleService,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            Mock.Of<ISubtitleExtractionService>(),
            Mock.Of<ISourceSubtitleResolver>(),
            NullLogger<SubtitleOutputBackfillService>.Instance);

        var result = await backfillService.RepairExistingAssOutputsAsync(
            movie,
            MediaType.Movie,
            request,
            [],
            "lingarr",
            "-ai-");
        var repairedAss = await File.ReadAllTextAsync(translatedAssPath);
        var repairedSrt = await File.ReadAllTextAsync(translatedSrtPath);

        Assert.Equal(1, result.RepairedFiles);
        Assert.Contains(@"Gram w {\i1}Brave Star{\i0}?", repairedAss);
        Assert.Contains("Gram w Brave Star?", repairedSrt);
        Assert.DoesNotContain("wBraveStar", repairedSrt);
    }

    [Fact]
    public async Task RepairExistingAssOutputsAsync_RebuildsDamagedSrtSidecarFromTranslatedAss()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedAssPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("Hello"));
        await File.WriteAllTextAsync(
            translatedAssPath,
            BuildAssContent("Czesc", secondText: "m 0 0 l 1 0 1 1 0 1"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildDamagedSrtContent());
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".ass,.srt",
            translatedAssPath,
            JsonSerializer.Serialize(new[] { translatedAssPath, translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();
        var request = await context.TranslationRequests.SingleAsync();
        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var backfillService = new SubtitleOutputBackfillService(
            context,
            subtitleService,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            Mock.Of<ISubtitleExtractionService>(),
            Mock.Of<ISourceSubtitleResolver>(),
            NullLogger<SubtitleOutputBackfillService>.Instance);

        var result = await backfillService.RepairExistingAssOutputsAsync(
            movie,
            MediaType.Movie,
            request,
            [],
            "lingarr",
            "-ai-");
        var repairedSrt = await File.ReadAllTextAsync(translatedSrtPath);

        Assert.Equal(1, result.RepairedFiles);
        Assert.Contains("Czesc", repairedSrt);
        Assert.DoesNotContain("m 0 0", repairedSrt);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfilledAssConvertsSrtLineBreaksToAssBreaks()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("Jusqu'a reussir, ce sera pas assez"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent($"Nawet gdy odniesiemy sukces, to nie{Environment.NewLine}wystarczy"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var ass = await File.ReadAllTextAsync(assPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.Contains(@"Nawet gdy odniesiemy sukces, to nie\Nwystarczy", ass);
        Assert.DoesNotContain($"Nawet gdy odniesiemy sukces, to nie{Environment.NewLine}wystarczy", ass);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_QueuesWhenBackfillAlignmentIsAmbiguous()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("{\\an7}Hello", secondText: "Second"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.BackfilledFiles);
        Assert.Equal(1, result.BackfillSkippedFiles);
        Assert.Equal(1, result.QueuedForRetranslation);
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass")));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_QueuesVerifierFlaggedDamagedPlainOutput()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("Hello"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent(@"{\an7}Czesc"));
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            sourceAssPath);
        await context.SaveChangesAsync();

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.BackfilledFiles);
        Assert.Equal(1, result.BackfillSkippedFiles);
        Assert.Equal(1, result.QueuedForRetranslation);
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass")));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_BackfillsMissingAssFromMatchingEmbeddedSourceSnapshot()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var missingExtractedSourcePath = Path.Combine(tempDirectory.Path, "missing.en.ass");
        var recoveredSourcePath = Path.Combine(tempDirectory.Path, "movie.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(recoveredSourcePath, BuildAssContent("{\\an7}Hello"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        context.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 3,
            Language = "en",
            CodecName = "ass",
            IsTextBased = true
        });
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            missingExtractedSourcePath,
            SourceSubtitleSnapshot.EmbeddedType,
            "same-fingerprint",
            3);
        await context.SaveChangesAsync();

        var sourceSubtitleResolver = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(recoveredSourcePath);

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1,
            sourceSubtitleResolver: sourceSubtitleResolver.Object);

        var result = await service.ReconcileLibraryOutputsAsync();
        var assPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass");
        var ass = await File.ReadAllTextAsync(assPath);

        Assert.Equal(1, result.BackfilledFiles);
        Assert.Equal(1, result.BackfilledFromEmbeddedSourceFiles);
        Assert.Equal(0, result.QueuedTranslations);
        Assert.Equal(0, result.QueuedForRetranslation);
        Assert.Contains(@"{\an7}Czesc", ass);
        sourceSubtitleResolver.Verify(
            service => service.ResolveReadableSourcePathAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_QueuesWhenEmbeddedSourceSnapshotDiffers()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var missingExtractedSourcePath = Path.Combine(tempDirectory.Path, "missing.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        context.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 3,
            Language = "en",
            CodecName = "ass",
            IsTextBased = true
        });
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            missingExtractedSourcePath,
            SourceSubtitleSnapshot.EmbeddedType,
            "old-fingerprint",
            3);
        await context.SaveChangesAsync();

        var sourceSubtitleResolver = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1,
            sourceSubtitleResolver: sourceSubtitleResolver.Object);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.BackfilledFiles);
        Assert.Equal(1, result.BackfillSkippedFiles);
        Assert.Equal(1, result.QueuedForRetranslation);
        sourceSubtitleResolver.Verify(
            service => service.ResolveReadableSourcePathAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_QueuesWhenEmbeddedSourceExtractionFails()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var missingExtractedSourcePath = Path.Combine(tempDirectory.Path, "missing.en.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(translatedSrtPath, BuildSrtContent("Czesc"));
        context.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 3,
            Language = "en",
            CodecName = "ass",
            IsTextBased = true
        });
        AddCompletedRequest(
            context,
            movie.Id,
            ".ass",
            ".srt",
            translatedSrtPath,
            JsonSerializer.Serialize(new[] { translatedSrtPath }),
            missingExtractedSourcePath,
            SourceSubtitleSnapshot.EmbeddedType,
            "same-fingerprint",
            3);
        await context.SaveChangesAsync();

        var sourceSubtitleResolver = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = BuildServiceWithRealSubtitleService(
            context,
            subtitleOutputMode: "both",
            queuedTranslations: 1,
            sourceSubtitleResolver: sourceSubtitleResolver.Object);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.BackfilledFiles);
        Assert.Equal(1, result.BackfillSkippedFiles);
        Assert.Equal(1, result.QueuedForRetranslation);
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "movie.pl.lingarr.ass")));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_DoesNotDeleteSecondSrtForSrtSourceWithBothMode()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        var srtPath = Path.Combine(tempDirectory.Path, "movie.pl.lingarr.srt");
        await File.WriteAllTextAsync(srtPath, "srt");
        AddCompletedRequest(
            context,
            movie.Id,
            ".srt",
            ".srt",
            srtPath,
            JsonSerializer.Serialize(new[] { srtPath }));
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "both",
            subtitles: [],
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(srtPath));
    }

    [Fact]
    public async Task ReconcileLibraryOutputsAsync_ReportsActiveRequestsWhenProcessorDoesNotQueue()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = AddMovie(context, tempDirectory.Path);
        context.TranslationRequests.Add(new TranslationRequest
        {
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:1:active",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = BuildService(
            context,
            subtitleOutputMode: "both",
            subtitles: [],
            queuedTranslations: 0);

        var result = await service.ReconcileLibraryOutputsAsync();

        Assert.Equal(1, result.SkippedActiveRequests);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static Movie AddMovie(LingarrDbContext context, string path)
    {
        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = path,
            DateAdded = DateTime.UtcNow
        };
        context.Movies.Add(movie);
        context.SaveChanges();
        return movie;
    }

    private static void AddCompletedRequest(
        LingarrDbContext context,
        int mediaId,
        string sourceFormat,
        string generatedFormats,
        string translatedSubtitle,
        string? generatedSubtitlePaths = null,
        string? subtitleToTranslate = null,
        string? sourceSnapshotType = null,
        string? sourceSnapshotFingerprint = null,
        int? sourceSnapshotStreamIndex = null)
    {
        context.TranslationRequests.Add(new TranslationRequest
        {
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = $"library:Movie:{mediaId}:{sourceFormat}",
            MediaId = mediaId,
            MediaType = MediaType.Movie,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = subtitleToTranslate ?? Path.ChangeExtension(translatedSubtitle, sourceFormat),
            TranslatedSubtitle = translatedSubtitle,
            SourceSubtitleFormat = sourceFormat,
            RequiredOutputFormats = generatedFormats,
            GeneratedOutputFormats = generatedFormats,
            GeneratedSubtitlePaths = generatedSubtitlePaths,
            SourceSnapshotType = sourceSnapshotType,
            SourceSnapshotFingerprint = sourceSnapshotFingerprint,
            SourceSnapshotStreamIndex = sourceSnapshotStreamIndex,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        });
    }

    private static SubtitleOutputReconciliationService BuildService(
        LingarrDbContext context,
        string subtitleOutputMode,
        List<Subtitles> subtitles,
        int queuedTranslations)
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(subtitleOutputMode);
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTag))
            .ReturnsAsync("lingarr");
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTagShort))
            .ReturnsAsync("-ai-");

        var subtitleService = new Mock<ISubtitleService>();
        subtitleService
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);
        var sourceSnapshotService = new Mock<ISourceSubtitleSnapshotService>();
        var extractionService = new Mock<ISubtitleExtractionService>();

        var mediaSubtitleProcessor = new Mock<IMediaSubtitleProcessor>();
        mediaSubtitleProcessor
            .Setup(service => service.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                true,
                false,
                true))
            .ReturnsAsync(queuedTranslations);

        return new SubtitleOutputReconciliationService(
            context,
            settings.Object,
            subtitleService.Object,
            new SubtitleOutputBackfillService(
                context,
                subtitleService.Object,
                sourceSnapshotService.Object,
                extractionService.Object,
                Mock.Of<ISourceSubtitleResolver>(),
                NullLogger<SubtitleOutputBackfillService>.Instance),
            mediaSubtitleProcessor.Object,
            NullLogger<SubtitleOutputReconciliationService>.Instance);
    }

    private static SubtitleOutputReconciliationService BuildServiceWithRealSubtitleService(
        LingarrDbContext context,
        string subtitleOutputMode,
        int queuedTranslations,
        ISourceSubtitleSnapshotService? sourceSnapshotService = null,
        ISubtitleExtractionService? extractionService = null,
        ISourceSubtitleResolver? sourceSubtitleResolver = null)
    {
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(subtitleOutputMode);
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTag))
            .ReturnsAsync("lingarr");
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTagShort))
            .ReturnsAsync("-ai-");

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var mediaSubtitleProcessor = new Mock<IMediaSubtitleProcessor>();
        mediaSubtitleProcessor
            .Setup(service => service.ProcessMediaForceAsync(
                It.IsAny<IMedia>(),
                It.IsAny<MediaType>(),
                true,
                false,
                true))
            .ReturnsAsync(queuedTranslations);

        return new SubtitleOutputReconciliationService(
            context,
            settings.Object,
            subtitleService,
            new SubtitleOutputBackfillService(
                context,
                subtitleService,
                sourceSnapshotService ?? Mock.Of<ISourceSubtitleSnapshotService>(),
                extractionService ?? Mock.Of<ISubtitleExtractionService>(),
                sourceSubtitleResolver ?? Mock.Of<ISourceSubtitleResolver>(),
                NullLogger<SubtitleOutputBackfillService>.Instance),
            mediaSubtitleProcessor.Object,
            NullLogger<SubtitleOutputReconciliationService>.Instance);
    }

    private static string BuildAssContent(
        string text,
        string style = "Sign",
        string? secondText = null)
    {
        var secondDialogue = secondText == null
            ? string.Empty
            : Environment.NewLine + $"Dialogue: Marked=0,0:00:04.00,0:00:05.00,{style},NTP,0000,0000,0000,!Effect,{secondText}";

        return $"""
               [Script Info]
               Title: Test
               ScriptType: v4.00+
               WrapStyle: 0

               [V4+ Styles]
               Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
               Style: {style},Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,7,10,10,10,1

               [Events]
               Format: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
               Dialogue: Marked=0,0:00:01.00,0:00:03.00,{style},NTP,0000,0000,0000,!Effect,{text}{secondDialogue}
               """;
    }

    private static string BuildSrtContent(string text)
    {
        return $"""
               1
               00:00:01,000 --> 00:00:03,000
               {text}

               """;
    }

    private static string BuildDamagedSrtContent()
    {
        return """
               1
               00:00:01,000 --> 00:00:03,000
               Czesc

               2
               00:00:04,000 --> 00:00:05,000
               m 0 0 l 1 0 1 1 0 1

               3
               00:00:06,000 --> 00:00:07,000
               m 0 0 l 1 0 1 1 0 1

               """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
