using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Subtitle;
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
            Mock.Of<ISubtitleOcrService>(),
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
            Mock.Of<ISubtitleOcrService>(),
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
            Mock.Of<ISubtitleOcrService>(),
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
            Mock.Of<ISubtitleOcrService>(),
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

    [Fact]
    public async Task ResolveReadableSourcePathAsync_AdoptsSoleVideoFileWhenDbFileNameIsStale()
    {
        var mediaPath = Path.Combine(_tempDirectory, "actual.release.mkv");
        await File.WriteAllTextAsync(mediaPath, "replacement release");

        var movie = new Movie
        {
            Id = 104,
            RadarrId = 104,
            Title = "Movie",
            FileName = "stale.file.name.mkv", // not on disk
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractedPath = Path.Combine(_tempDirectory, "extracted.srt");
        await File.WriteAllTextAsync(extractedPath, "1\n00:00:01,000 --> 00:00:02,000\nHello");

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                null))
            .ReturnsAsync(extractedPath);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 204,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(extractedPath, resolvedPath);
        var persisted = await _dbContext.Movies.AsNoTracking().SingleAsync(item => item.Id == movie.Id);
        Assert.Equal("actual.release.mkv", persisted.FileName);
        extractionService.Verify(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
            movie.Id,
            MediaType.Movie,
            "eng",
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_FailsWhenMultipleVideoFilesExist()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "first.mkv"), "first");
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "second.mkv"), "second");

        var movie = new Movie
        {
            Id = 105,
            RadarrId = 105,
            Title = "Movie",
            FileName = "stale.file.name.mkv", // not on disk
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .ReturnsAsync((string?)null);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 205,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Null(resolvedPath);
        var persisted = await _dbContext.Movies.AsNoTracking().SingleAsync(item => item.Id == movie.Id);
        Assert.Equal("stale.file.name.mkv", persisted.FileName);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_ResyncsEmbeddedRowsWhenMediaFileReplaced()
    {
        var mediaPath = Path.Combine(_tempDirectory, "replacement.release.mkv");
        await File.WriteAllTextAsync(mediaPath, "replacement release");

        var movie = new Movie
        {
            Id = 106,
            RadarrId = 106,
            Title = "Movie",
            FileName = "old.release.mkv", // not on disk
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

        var extractedPath = Path.Combine(_tempDirectory, "extracted.srt");
        await File.WriteAllTextAsync(extractedPath, "1\n00:00:01,000 --> 00:00:02,000\nHello");

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                0))
            .ReturnsAsync(extractedPath);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 206,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotStreamIndex = 0
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(extractedPath, resolvedPath);
        extractionService.Verify(
            service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()),
            Times.Once);
        var persisted = await _dbContext.Movies.AsNoTracking().SingleAsync(item => item.Id == movie.Id);
        Assert.Equal("replacement.release.mkv", persisted.FileName);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_DoesNotResyncRowsWhenDbFileNameMatchesDisk()
    {
        var mediaPath = Path.Combine(_tempDirectory, "matching.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged release");

        var movie = new Movie
        {
            Id = 107,
            RadarrId = 107,
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

        var extractedPath = Path.Combine(_tempDirectory, "extracted.srt");
        await File.WriteAllTextAsync(extractedPath, "1\n00:00:01,000 --> 00:00:02,000\nHello");

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                0))
            .ReturnsAsync(extractedPath);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 207,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotStreamIndex = 0
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(extractedPath, resolvedPath);
        extractionService.Verify(
            service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_RerunsOcrWhenOcrCacheFileMissingButMediaUnchanged()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media snapshot");

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var ocrPath = cacheService.GetOcrCachePath(
            mediaId: 108,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            language: "eng");
        await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);

        var movie = new Movie
        {
            Id = 108,
            RadarrId = 108,
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
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = ocrPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        // Simulate the OCR output being deleted while the request was paused
        // (manual cleanup, expired snapshot cleanup, ...) with the media unchanged.
        File.Delete(ocrPath);
        Assert.True(cacheService.IsSourceSnapshotCurrent(ocrPath, mediaPath));

        var ocrService = new Mock<ISubtitleOcrService>();
        ocrService
            .Setup(service => service.RunOcrAsync(
                movie.Id,
                MediaType.Movie,
                0,
                false,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                File.WriteAllText(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
                return Task.FromResult(new SubtitleOcrResult
                {
                    Success = true,
                    Status = SubtitleOcrStatus.Succeeded,
                    ExtractedPath = ocrPath
                });
            });

        var extractionService = new Mock<ISubtitleExtractionService>();
        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 208,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath,
            SourceSnapshotStreamIndex = 0
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(ocrPath, resolvedPath);
        Assert.True(File.Exists(ocrPath));
        ocrService.Verify(service => service.RunOcrAsync(
            movie.Id,
            MediaType.Movie,
            0,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ResolveReadableSourcePathAsync_DoesNotRerunOcrWhenMediaChanged()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "old media snapshot");

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var ocrPath = cacheService.GetOcrCachePath(
            mediaId: 109,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            language: "eng");
        await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);

        var movie = new Movie
        {
            Id = 109,
            RadarrId = 109,
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
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = ocrPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        // The media was replaced while the request was paused and the OCR output is
        // already gone: the stale OCR must NOT be regenerated or translated.
        await File.WriteAllTextAsync(mediaPath, "new media snapshot");
        File.Delete(ocrPath);
        Assert.False(cacheService.IsSourceSnapshotCurrent(ocrPath, mediaPath));

        var ocrService = new Mock<ISubtitleOcrService>();
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
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 209,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Null(resolvedPath);
        ocrService.Verify(
            service => service.RunOcrAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                null),
            Times.Once);
        var clearedSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync();
        Assert.Equal(SubtitleOcrStatus.NotStarted, clearedSubtitle.OcrStatus);
        Assert.Null(clearedSubtitle.OcrExtractedPath);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_DoesNotRerunOcrForMissingExtractedCacheFile()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media snapshot");

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var extractedPath = cacheService.GetCachePath(
            mediaId: 110,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            codecName: "subrip",
            language: "eng");
        await File.WriteAllTextAsync(extractedPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        cacheService.RecordSourceSnapshot(extractedPath, mediaPath);

        var movie = new Movie
        {
            Id = 110,
            RadarrId = 110,
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
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = extractedPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        // A missing extracted-text cache file is recovered by re-extraction, not OCR.
        File.Delete(extractedPath);

        var reExtractedPath = Path.Combine(_tempDirectory, "re-extracted.srt");
        await File.WriteAllTextAsync(reExtractedPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var ocrService = new Mock<ISubtitleOcrService>();
        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                0))
            .ReturnsAsync(reExtractedPath);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 210,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = extractedPath,
            SourceSnapshotStreamIndex = 0
        };

        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Equal(reExtractedPath, resolvedPath);
        ocrService.Verify(
            service => service.RunOcrAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                movie.Id,
                MediaType.Movie,
                "eng",
                null,
                0),
            Times.Once);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_FallsBackWhenRerunOcrThrows()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media snapshot");

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var ocrPath = cacheService.GetOcrCachePath(
            mediaId: 111,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            language: "eng");
        await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);

        var movie = new Movie
        {
            Id = 111,
            RadarrId = 111,
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
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = ocrPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        File.Delete(ocrPath);

        var ocrService = new Mock<ISubtitleOcrService>();
        ocrService
            .Setup(service => service.RunOcrAsync(
                movie.Id,
                MediaType.Movie,
                0,
                false,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Mirror the real service: a failed re-OCR demotes the row to Failed and
                // persists it before throwing.
                var row = await _dbContext.EmbeddedSubtitles.SingleAsync(
                    subtitle => subtitle.MovieId == movie.Id && subtitle.StreamIndex == 0);
                row.OcrStatus = SubtitleOcrStatus.Failed;
                row.OcrError = "ocr engine unavailable";
                await _dbContext.SaveChangesAsync();
                throw new InvalidOperationException("ocr engine unavailable");
            });

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .ReturnsAsync((string?)null);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 211,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath,
            SourceSnapshotStreamIndex = 0
        };

        // Recovery failure must not throw and must not loop: it falls back to the
        // existing resolution behavior.
        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Null(resolvedPath);
        ocrService.Verify(service => service.RunOcrAsync(
            movie.Id,
            MediaType.Movie,
            0,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()),
            Times.Once);

        // The failed re-OCR demoted the row, the resolver restored the captured Succeeded
        // state (keeping future recovery possible), but the extraction fallback then resets
        // the row because the OCR artifact is missing (IsStaleManagedArtifact) — the correct
        // end state for a missing artifact is a fresh re-probe (NotStarted).
        var verifyOptions = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var verifyContext = new LingarrDbContext(verifyOptions);
        var restored = await verifyContext.EmbeddedSubtitles.AsNoTracking().SingleAsync();
        Assert.Equal(SubtitleOcrStatus.NotStarted, restored.OcrStatus);
        Assert.Null(restored.OcrError);
    }

    [Fact]
    public async Task ResolveReadableSourcePathAsync_RestoresOcrRowStatusWhenRerunOcrFails()
    {
        var mediaPath = Path.Combine(_tempDirectory, "episode.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media snapshot");

        var cacheService = new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
        var ocrPath = cacheService.GetOcrCachePath(
            mediaId: 112,
            mediaType: MediaType.Movie,
            streamIndex: 0,
            language: "eng");
        await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);

        var movie = new Movie
        {
            Id = 112,
            RadarrId = 112,
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
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = ocrPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        File.Delete(ocrPath);

        var ocrService = new Mock<ISubtitleOcrService>();
        ocrService
            .Setup(service => service.RunOcrAsync(
                movie.Id,
                MediaType.Movie,
                0,
                false,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Mirror the real service: a failed re-OCR demotes the row to Failed and
                // persists it before returning the failure result.
                var row = await _dbContext.EmbeddedSubtitles.SingleAsync(
                    subtitle => subtitle.MovieId == movie.Id && subtitle.StreamIndex == 0);
                row.OcrStatus = SubtitleOcrStatus.Failed;
                row.OcrError = "ocr engine unavailable";
                await _dbContext.SaveChangesAsync();
                return new SubtitleOcrResult
                {
                    Success = false,
                    Status = SubtitleOcrStatus.Failed,
                    Error = "ocr engine unavailable"
                };
            });

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()))
            .ReturnsAsync((string?)null);

        var resolver = new SourceSubtitleResolver(
            _dbContext,
            Mock.Of<ISubtitleService>(),
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance);
        var request = new TranslationRequest
        {
            Id = 212,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath,
            SourceSnapshotStreamIndex = 0
        };

        // Recovery failure falls back to the existing resolution behavior, never throws,
        // and the extraction fallback resets the row (artifact missing) so it is re-probed
        // fresh — the correct end state for a missing OCR artifact.
        var resolvedPath = await resolver.ResolveReadableSourcePathAsync(request);

        Assert.Null(resolvedPath);
        extractionService.Verify(
            service => service.TryExtractEmbeddedSubtitleForRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<int?>()),
            Times.Once);

        var restoreOptions = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var restoreContext = new LingarrDbContext(restoreOptions);
        var restored = await restoreContext.EmbeddedSubtitles.AsNoTracking().SingleAsync();
        Assert.Equal(SubtitleOcrStatus.NotStarted, restored.OcrStatus);
        Assert.Null(restored.OcrError);
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
