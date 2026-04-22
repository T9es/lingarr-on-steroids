using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
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
        string? generatedSubtitlePaths = null)
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
            SubtitleToTranslate = Path.ChangeExtension(translatedSubtitle, sourceFormat),
            TranslatedSubtitle = translatedSubtitle,
            SourceSubtitleFormat = sourceFormat,
            RequiredOutputFormats = generatedFormats,
            GeneratedOutputFormats = generatedFormats,
            GeneratedSubtitlePaths = generatedSubtitlePaths,
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
            mediaSubtitleProcessor.Object,
            NullLogger<SubtitleOutputReconciliationService>.Instance);
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
