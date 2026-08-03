using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public sealed class SourceSubtitleResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly string _tempDirectory;

    public SourceSubtitleResolverTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_ReextractsManagedCacheAfterMediaReplacement()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "old media snapshot");

        var movie = new Movie
        {
            Id = 100,
            RadarrId = 100,
            Title = "Movie",
            FileName = Path.GetFileName(mediaPath),
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var cachedSourcePath = cacheService.GetCachePath(
            movie.Id,
            MediaType.Movie,
            streamIndex: 0,
            codecName: "subrip",
            language: "eng");
        await File.WriteAllTextAsync(cachedSourcePath, "old subtitle snapshot");
        cacheService.RecordSourceSnapshot(cachedSourcePath, mediaPath);
        Assert.True(cacheService.IsCurrentForSource(cachedSourcePath, mediaPath));

        var replacementPath = Path.Combine(_tempDirectory, "episode.replacement.mkv");
        await File.WriteAllTextAsync(replacementPath, "new media snapshot");
        File.Move(replacementPath, mediaPath, overwrite: true);
        Assert.False(cacheService.IsCurrentForSource(cachedSourcePath, mediaPath));

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                0))
            .Returns(() =>
            {
                Assert.False(File.Exists(cachedSourcePath));
                File.WriteAllText(cachedSourcePath, "new subtitle snapshot");
                cacheService.RecordSourceSnapshot(cachedSourcePath, mediaPath);
                return Task.FromResult<string?>(cachedSourcePath);
            });

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 200,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = cachedSourcePath,
            SourceSnapshotStreamIndex = 0
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(cachedSourcePath, resolvedPath);
        Assert.True(cacheService.IsCurrentForSource(cachedSourcePath, mediaPath));
        extractionService.Verify(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
            movie.Id,
            MediaType.Movie,
            "eng",
            null,
            0), Times.Once);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_ReusesManagedCacheWhenMediaIsUnchanged()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media snapshot");

        var movie = new Movie
        {
            Id = 101,
            RadarrId = 101,
            Title = "Movie",
            FileName = Path.GetFileName(mediaPath),
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var cachedSourcePath = cacheService.GetCachePath(
            movie.Id,
            MediaType.Movie,
            streamIndex: 0,
            codecName: "subrip",
            language: "eng");
        await File.WriteAllTextAsync(cachedSourcePath, "cached subtitle");
        cacheService.RecordSourceSnapshot(cachedSourcePath, mediaPath);

        var extractionService = new Mock<ISubtitleExtractionService>();
        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 202,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = cachedSourcePath
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(cachedSourcePath, resolvedPath);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task IsCurrentForSource_RejectsSameSignatureMediaReplacement()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "old media content");
        var originalLastWriteUtc = File.GetLastWriteTimeUtc(mediaPath);
        var originalCreationUtc = File.GetCreationTimeUtc(mediaPath);
        var originalLength = new FileInfo(mediaPath).Length;

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var cachedSourcePath = cacheService.GetCachePath(
            mediaId: 102,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            codecName: "subrip",
            language: "eng");
        await File.WriteAllTextAsync(cachedSourcePath, "cached subtitle");
        cacheService.RecordSourceSnapshot(cachedSourcePath, mediaPath);

        await File.WriteAllTextAsync(mediaPath, "new media content");
        File.SetLastWriteTimeUtc(mediaPath, originalLastWriteUtc);
        File.SetCreationTimeUtc(mediaPath, originalCreationUtc);

        Assert.Equal(originalLength, new FileInfo(mediaPath).Length);
        Assert.False(cacheService.IsCurrentForSource(cachedSourcePath, mediaPath));
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_InvalidatesStaleManagedOcrAfterMediaReplacement()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "old media snapshot");

        var movie = new Movie
        {
            Id = 103,
            RadarrId = 103,
            Title = "Movie",
            FileName = Path.GetFileName(mediaPath),
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var ocrPath = cacheService.GetOcrCachePath(
            movie.Id,
            MediaType.Movie,
            streamIndex: 0,
            language: "eng");
        await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nOld OCR\n");
        var subtitle = await _dbContext.EmbeddedSubtitles.SingleAsync();
        subtitle.OcrExtractedPath = ocrPath;
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);
        await _dbContext.SaveChangesAsync();

        await File.WriteAllTextAsync(mediaPath, "new media snapshot");

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                It.IsAny<int?>()))
            .Returns((int _, MediaType _, string _, List<int>? _, int? preferredStreamIndex) =>
            {
                Assert.Null(preferredStreamIndex);
                return Task.FromResult<string?>(null);
            });

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 203,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Null(resolvedPath);
        Assert.False(File.Exists(ocrPath));
        var clearedSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync();
        Assert.Equal(SubtitleOcrStatus.NotStarted, clearedSubtitle.OcrStatus);
        Assert.Null(clearedSubtitle.OcrExtractedPath);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                null),
            Times.Once);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_ReturnsReadableSidecarWithoutCacheValidation()
    {
        var sidecarPath = Path.Combine(_tempDirectory, "movie.eng.srt");
        await File.WriteAllTextAsync(sidecarPath, "1\n00:00:01,000 --> 00:00:02,000\nHello");

        var extractionService = new Mock<ISubtitleExtractionService>();
        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 201,
            Title = "Movie",
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sidecarPath
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(sidecarPath, resolvedPath);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()),
            Times.Never);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
