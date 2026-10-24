using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
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
    public async Task PrepareTranslationSourceAsync_ReplacesSparseCachedPreferredStreamWithSameLanguageDialogue()
    {
        var mediaPath = Path.Combine(_tempDirectory, "movie.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(120, mediaPath);
        var cacheService = CreateCacheService();
        var sparsePath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        await File.WriteAllTextAsync(sparsePath, CreateSrt(13));
        cacheService.RecordSourceSnapshot(sparsePath, mediaPath);

        var sparseSubtitle = CreateTextSubtitle(movie, 0, "eng", "Full", sparsePath);
        var dialogueSubtitle = CreateTextSubtitle(movie, 1, "eng", "English", null);
        movie.EmbeddedSubtitles.Add(sparseSubtitle);
        movie.EmbeddedSubtitles.Add(dialogueSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                1,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(80));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 220,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sparsePath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(80, prepared.EntryCount);
        var canonicalPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 1, "subrip", "eng");
        Assert.NotEqual(canonicalPath, prepared.Path);
        Assert.StartsWith(
            $"{Path.GetFileNameWithoutExtension(canonicalPath)}.prepare-",
            Path.GetFileNameWithoutExtension(prepared.Path),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(sparsePath));
        Assert.Equal(prepared.Path, dialogueSubtitle.ExtractedPath);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RefreshesSubtitleMetadataBeforeFilteringReplacementMedia()
    {
        var mediaPath = Path.Combine(_tempDirectory, "replacement.prepare.mkv");
        await File.WriteAllTextAsync(mediaPath, "replacement media source");
        var movie = new Movie
        {
            Id = 247,
            RadarrId = 247,
            Title = "Movie",
            FileName = "old.release.mkv",
            Path = _tempDirectory,
            DateAdded = DateTime.UtcNow
        };
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "Old English", null));
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "fra", "Old French", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var cacheService = CreateCacheService();
        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns((Movie syncedMovie) =>
            {
                var firstSubtitle = syncedMovie.EmbeddedSubtitles.Single(subtitle => subtitle.StreamIndex == 0);
                firstSubtitle.Language = "spa";
                firstSubtitle.Title = "Replacement Spanish";
                var secondSubtitle = syncedMovie.EmbeddedSubtitles.Single(subtitle => subtitle.StreamIndex == 1);
                secondSubtitle.Language = "eng";
                secondSubtitle.Title = "Replacement English";
                return Task.CompletedTask;
            });
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                1,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                await File.WriteAllTextAsync(outputPath, CreateSrt(80));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return outputPath;
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 247,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotStreamIndex = 0
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal("Replacement English", prepared.SelectedSubtitle?.Title);
        Assert.Equal(80, prepared.EntryCount);
        extractionService.Verify(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()), Times.Once);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            1,
            It.IsAny<string>(),
            "subrip"), Times.Once);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            0,
            It.IsAny<string>(),
            "subrip"), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_ManualStreamOverridesDifferentExistingCache()
    {
        var mediaPath = Path.Combine(_tempDirectory, "manual.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(121, mediaPath);
        var cacheService = CreateCacheService();
        var cachedAutomaticPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 1, "subrip", "eng");
        await File.WriteAllTextAsync(cachedAutomaticPath, CreateSrt(90));
        cacheService.RecordSourceSnapshot(cachedAutomaticPath, mediaPath);

        var manualSubtitle = CreateTextSubtitle(movie, 0, "eng", "English", null);
        var automaticSubtitle = CreateTextSubtitle(movie, 1, "eng", "English", cachedAutomaticPath);
        movie.EmbeddedSubtitles.Add(manualSubtitle);
        movie.EmbeddedSubtitles.Add(automaticSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(60));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 221,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = cachedAutomaticPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: true,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.True(prepared.IsManualSelection);
        Assert.Equal(0, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(60, prepared.EntryCount);
        Assert.NotEqual(cachedAutomaticPath, prepared.Path);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            0,
            It.IsAny<string>(),
            "subrip"), Times.Once);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            1,
            It.IsAny<string>(),
            "subrip"), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_ManualSparseStreamDoesNotFallbackToAnotherStream()
    {
        var mediaPath = Path.Combine(_tempDirectory, "manual-sparse.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(122, mediaPath);
        var cacheService = CreateCacheService();
        var fallbackPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 1, "subrip", "eng");
        await File.WriteAllTextAsync(fallbackPath, CreateSrt(90));
        cacheService.RecordSourceSnapshot(fallbackPath, mediaPath);

        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "Full", null));
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English", fallbackPath));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(13));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 222,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = fallbackPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: true,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: false,
                    SupplementalTranslationEnabled: false)));
        Assert.Contains("13 entries", exception.Message);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            1,
            It.IsAny<string>(),
            "subrip"), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_UsesDurationAwareFloorForShortFormDialogue()
    {
        var mediaPath = Path.Combine(_tempDirectory, "short-form.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(131, mediaPath);
        var cacheService = CreateCacheService();
        var shortPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        await File.WriteAllTextAsync(shortPath, CreateSrt(33));
        cacheService.RecordSourceSnapshot(shortPath, mediaPath);

        var shortSubtitle = CreateTextSubtitle(movie, 0, "eng", "English", shortPath);
        movie.EmbeddedSubtitles.Add(shortSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var request = new TranslationRequest
        {
            Id = 231,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = shortPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };
        var options = new TranslationSourcePreparationOptions(
            HasManualStreamSelection: false,
            AllowCaptionFallback: false,
            IsSupplementalRequest: false,
            SupplementalTranslationEnabled: false);

        var durationProvider = new Mock<IMediaDurationProvider>();
        durationProvider
            .Setup(provider => provider.GetDurationSecondsAsync(mediaPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(270);
        var durationAwareResolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            mediaDurationProvider: durationProvider.Object);

        var prepared = await durationAwareResolver.PrepareTranslationSourceAsync(request, options);

        Assert.NotNull(prepared);
        Assert.Equal(33, prepared.EntryCount);
        Assert.Equal(0, prepared.SelectedSubtitle?.StreamIndex);
        durationProvider.Verify(
            provider => provider.GetDurationSecondsAsync(mediaPath, It.IsAny<CancellationToken>()),
            Times.Once);

        var legacyResolver = CreateResolver(Mock.Of<ISubtitleExtractionService>(), cacheService);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            legacyResolver.PrepareTranslationSourceAsync(request, options));

        Assert.Contains("33 entries", exception.Message);
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
    public async Task PrepareTranslationSourceAsync_DoesNotUseCrossLanguageFallback()
    {
        var mediaPath = Path.Combine(_tempDirectory, "wrong-language.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(123, mediaPath);
        var cacheService = CreateCacheService();
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "por", "Portuguese", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 223,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false)));

        extractionService.Verify(service => service.ExtractSubtitleToFile(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_ReplacesStaleCacheWithoutOverwritingItAndReusesValidatedTempPath()
    {
        var mediaPath = Path.Combine(_tempDirectory, "replacement.mkv");
        var oldMediaPath = Path.Combine(_tempDirectory, "old-replacement.mkv");
        await File.WriteAllTextAsync(mediaPath, "current media source");
        await File.WriteAllTextAsync(oldMediaPath, "old media source");
        var movie = CreateMovie(124, mediaPath);
        var cacheService = CreateCacheService();
        var stalePath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        var staleContents = CreateSrt(90);
        await File.WriteAllTextAsync(stalePath, staleContents);
        cacheService.RecordSourceSnapshot(stalePath, oldMediaPath);
        Assert.True(cacheService.IsManagedCachePath(stalePath));
        Assert.False(cacheService.IsCurrentForSource(stalePath, mediaPath));

        var subtitle = CreateTextSubtitle(movie, 0, "eng", "English", stalePath);
        movie.EmbeddedSubtitles.Add(subtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                await File.WriteAllTextAsync(outputPath, CreateSrt(80));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return outputPath;
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 224,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = stalePath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var firstPrepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(firstPrepared);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            0,
            It.IsAny<string>(),
            "subrip"), Times.Once);
        Assert.NotEqual(stalePath, firstPrepared.Path);
        Assert.Equal(staleContents, await File.ReadAllTextAsync(stalePath));
        Assert.True(File.Exists(firstPrepared.Path));

        request.SubtitleToTranslate = firstPrepared.Path;
        var secondPrepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(secondPrepared);
        Assert.Equal(firstPrepared.Path, secondPrepared.Path);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            0,
            It.IsAny<string>(),
            "subrip"), Times.Once);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RestoresMissingSuccessfulOcrCacheForSameMediaSnapshot()
    {
        var mediaPath = Path.Combine(_tempDirectory, "ocr-recovery.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media source");
        var movie = CreateMovie(126, mediaPath);
        var cacheService = CreateCacheService();
        var ocrPath = cacheService.GetOcrCachePath(movie.Id, MediaType.Movie, 0, "eng");
        await File.WriteAllTextAsync(ocrPath, CreateSrt(50));
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrCueCount = 50,
            OcrExtractedPath = ocrPath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

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
            .Returns(async () =>
            {
                await File.WriteAllTextAsync(ocrPath, CreateSrt(50));
                cacheService.RecordSourceSnapshot(ocrPath, mediaPath);
                return new SubtitleOcrResult
                {
                    Success = true,
                    Status = SubtitleOcrStatus.Succeeded,
                    ExtractedPath = ocrPath
                };
            });

        var extractionService = new Mock<ISubtitleExtractionService>();
        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var resolver = new SourceSubtitleResolver(
            _dbContext,
            subtitleService,
            extractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            ocrService.Object,
            NullLogger<SourceSubtitleResolver>.Instance,
            new SubtitleSourceSelectionService(subtitleService, NullLogger<SubtitleSourceSelectionService>.Instance));
        var request = new TranslationRequest
        {
            Id = 226,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath,
            SourceSnapshotStreamIndex = 0
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(ocrPath, prepared.Path);
        Assert.Same(movie.EmbeddedSubtitles.Single(), prepared.SelectedSubtitle);
        Assert.Equal(50, prepared.EntryCount);
        Assert.Equal(SubtitleOcrStatus.Succeeded, movie.EmbeddedSubtitles.Single().OcrStatus);
        ocrService.Verify(service => service.RunOcrAsync(
            movie.Id,
            MediaType.Movie,
            0,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RestoresMissingOcrCacheForSameManualStream()
    {
        var mediaPath = Path.Combine(_tempDirectory, "manual-ocr-recovery.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media source");
        var movie = CreateMovie(240, mediaPath);
        var cacheService = CreateCacheService();
        var ocrPath = cacheService.GetOcrCachePath(movie.Id, MediaType.Movie, 0, "eng");
        await File.WriteAllTextAsync(ocrPath, CreateSrt(50));
        cacheService.RecordSourceSnapshot(ocrPath, mediaPath);
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English Full PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrCueCount = 50,
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
                await File.WriteAllTextAsync(ocrPath, CreateSrt(50));
                cacheService.RecordSourceSnapshot(ocrPath, mediaPath);
                return new SubtitleOcrResult
                {
                    Success = true,
                    Status = SubtitleOcrStatus.Succeeded,
                    ExtractedPath = ocrPath
                };
            });

        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            subtitleOcrService: ocrService.Object);
        var request = new TranslationRequest
        {
            Id = 241,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = ocrPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: true,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.True(prepared.IsManualSelection);
        Assert.Same(movie.EmbeddedSubtitles.Single(), prepared.SelectedSubtitle);
        Assert.Equal(50, prepared.EntryCount);
        ocrService.Verify(service => service.RunOcrAsync(
            movie.Id,
            MediaType.Movie,
            0,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_DoesNotRestoreMissingOcrCacheForDifferentManualStream()
    {
        var mediaPath = Path.Combine(_tempDirectory, "different-manual-ocr.mkv");
        await File.WriteAllTextAsync(mediaPath, "unchanged media source");
        var movie = CreateMovie(242, mediaPath);
        var cacheService = CreateCacheService();
        var oldOcrPath = cacheService.GetOcrCachePath(movie.Id, MediaType.Movie, 0, "eng");
        await File.WriteAllTextAsync(oldOcrPath, CreateSrt(50));
        cacheService.RecordSourceSnapshot(oldOcrPath, mediaPath);
        var oldOcrSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrCueCount = 50,
            OcrExtractedPath = oldOcrPath
        };
        var selectedPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 1, "subrip", "eng");
        await File.WriteAllTextAsync(selectedPath, CreateSrt(80));
        cacheService.RecordSourceSnapshot(selectedPath, mediaPath);
        movie.EmbeddedSubtitles.Add(oldOcrSubtitle);
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English Full", selectedPath));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();
        File.Delete(oldOcrPath);

        var ocrService = new Mock<ISubtitleOcrService>();
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            subtitleOcrService: ocrService.Object);
        var request = new TranslationRequest
        {
            Id = 243,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = oldOcrPath,
            SourceSnapshotStreamIndex = 1,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: true,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.True(prepared.IsManualSelection);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(selectedPath, prepared.Path);
        ocrService.Verify(service => service.RunOcrAsync(
            It.IsAny<int>(),
            It.IsAny<MediaType>(),
            It.IsAny<int>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_DeletesNewlyExtractedRejectedCandidate()
    {
        var mediaPath = Path.Combine(_tempDirectory, "candidate-cleanup.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(125, mediaPath);
        var cacheService = CreateCacheService();
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "Full", null));
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        string? rejectedPath = null;
        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                It.IsAny<int>(),
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int streamIndex, string outputPath, string _) =>
            {
                var entryCount = streamIndex == 0 ? 13 : 80;
                await File.WriteAllTextAsync(outputPath, CreateSrt(entryCount));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                if (streamIndex == 0)
                {
                    rejectedPath = outputPath;
                }

                return outputPath;
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 225,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.NotNull(rejectedPath);
        Assert.False(File.Exists(rejectedPath));
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_ConcurrentRejectedCleanupPreservesAcceptedExtraction()
    {
        var mediaPath = Path.Combine(_tempDirectory, "concurrent-prepare.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(248, mediaPath);
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "English", null));

        var databasePath = Path.Combine(_tempDirectory, "concurrent-prepare.db");
        var databaseOptions = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using (var seedContext = new LingarrDbContext(databaseOptions))
        {
            await seedContext.Database.EnsureCreatedAsync();
            seedContext.Movies.Add(movie);
            await seedContext.SaveChangesAsync();
        }

        var cacheService = CreateCacheService();
        var canonicalPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        var rejectedExtractionEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptedPreparationComplete = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? acceptedPath = null;
        string? rejectedPath = null;

        var acceptedExtractionService = new Mock<ISubtitleExtractionService>();
        acceptedExtractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                acceptedPath = outputPath;
                await rejectedExtractionEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await File.WriteAllTextAsync(outputPath, CreateSrt(80));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return outputPath;
            });

        var rejectedExtractionService = new Mock<ISubtitleExtractionService>();
        rejectedExtractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                rejectedPath = outputPath;
                rejectedExtractionEntered.SetResult(true);
                await acceptedPreparationComplete.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await File.WriteAllTextAsync(outputPath, CreateSrt(13));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return outputPath;
            });

        await using var acceptedContext = new LingarrDbContext(databaseOptions);
        await using var rejectedContext = new LingarrDbContext(databaseOptions);
        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var acceptedResolver = new SourceSubtitleResolver(
            acceptedContext,
            subtitleService,
            acceptedExtractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var rejectedResolver = new SourceSubtitleResolver(
            rejectedContext,
            subtitleService,
            rejectedExtractionService.Object,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance);
        var acceptedRequest = new TranslationRequest
        {
            Id = 248,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };
        var rejectedRequest = new TranslationRequest
        {
            Id = 249,
            Title = movie.Title,
            SourceLanguage = "eng",
            TargetLanguage = "pol",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };
        var options = new TranslationSourcePreparationOptions(
            HasManualStreamSelection: false,
            AllowCaptionFallback: false,
            IsSupplementalRequest: false,
            SupplementalTranslationEnabled: false);

        var acceptedTask = acceptedResolver.PrepareTranslationSourceAsync(acceptedRequest, options);
        var rejectedTask = rejectedResolver.PrepareTranslationSourceAsync(rejectedRequest, options);
        var accepted = await acceptedTask.WaitAsync(TimeSpan.FromSeconds(15));
        acceptedPreparationComplete.SetResult(true);
        var rejectedException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await rejectedTask.WaitAsync(TimeSpan.FromSeconds(15));
        });

        Assert.NotNull(accepted);
        Assert.Equal(80, accepted.EntryCount);
        Assert.NotNull(acceptedPath);
        Assert.NotNull(rejectedPath);
        Assert.NotEqual(acceptedPath, rejectedPath);
        Assert.NotEqual(canonicalPath, acceptedPath);
        Assert.NotEqual(canonicalPath, rejectedPath);
        Assert.True(File.Exists(acceptedPath));
        Assert.False(File.Exists(rejectedPath));
        Assert.False(File.Exists(canonicalPath));
        Assert.Contains("13 entries", rejectedException.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareTranslationSourceAsync_RejectsSupplementalRequestWhenFeatureIsDisabled(
        bool hasManualStreamSelection)
    {
        var subtitlePath = Path.Combine(_tempDirectory, "disabled-supplemental.srt");
        await File.WriteAllTextAsync(subtitlePath, CreateSrt(60));
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            CreateCacheService());
        var request = new TranslationRequest
        {
            Id = 227,
            Title = "External source",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = subtitlePath
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: hasManualStreamSelection,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: true,
                    SupplementalTranslationEnabled: false)));

        Assert.Contains("Supplemental subtitle translation is disabled", exception.Message);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_KeepsExternalSupplementalSourceAtItsExactPath()
    {
        var subtitlePath = Path.Combine(_tempDirectory, "external-supplemental.srt");
        await File.WriteAllTextAsync(subtitlePath, CreateSrt(18));
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            CreateCacheService());
        var request = new TranslationRequest
        {
            Id = 235,
            Title = "External source",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceDedupeKey = "supplemental:forced",
            SourceSubtitleType = SubtitleLanguageHelper.TypeForced,
            SubtitleToTranslate = subtitlePath,
            SourceSnapshotType = SourceSubtitleSnapshot.ExternalType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: true,
                SupplementalTranslationEnabled: true));

        Assert.NotNull(prepared);
        Assert.Equal(subtitlePath, prepared.Path);
        Assert.Null(prepared.SelectedSubtitle);
        Assert.Equal(18, prepared.EntryCount);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RejectsEmbeddedSupplementalRequestWithoutIdentity()
    {
        var mediaPath = Path.Combine(_tempDirectory, "supplemental-no-identity.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(236, mediaPath);
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "Signs/Songs", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            CreateCacheService());
        var request = new TranslationRequest
        {
            Id = 237,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceDedupeKey = "supplemental:signs-songs",
            SourceSubtitleType = SubtitleLanguageHelper.TypeSignsSongs,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: false,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: true,
                    SupplementalTranslationEnabled: true)));

        Assert.Contains("no verifiable embedded source identity", exception.Message);
        Assert.Contains("Create a new supplemental request", exception.Message);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RejectsManualSupplementalStreamDifferentFromPinnedIdentity()
    {
        var mediaPath = Path.Combine(_tempDirectory, "supplemental-manual-mismatch.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(238, mediaPath);
        var cacheService = CreateCacheService();
        var pinnedPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        await File.WriteAllTextAsync(pinnedPath, CreateSrt(16));
        cacheService.RecordSourceSnapshot(pinnedPath, mediaPath);
        var pinnedSubtitle = CreateTextSubtitle(movie, 0, "eng", "Signs/Songs", pinnedPath);
        pinnedSubtitle.IsForced = true;
        movie.EmbeddedSubtitles.Add(pinnedSubtitle);
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "Signs/Songs", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var snapshotService = CreateSnapshotService(subtitleService);
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            snapshotService);
        var request = new TranslationRequest
        {
            Id = 239,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceDedupeKey = "supplemental:signs-songs",
            SourceSubtitleType = SubtitleLanguageHelper.TypeSignsSongs,
            SubtitleToTranslate = pinnedPath,
            SourceSnapshotStreamIndex = 1,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType,
            SourceSnapshotIdentity = snapshotService.CreateEmbeddedSnapshot(pinnedSubtitle, "en").Identity
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: true,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: true,
                    SupplementalTranslationEnabled: true)));

        Assert.Contains("does not match this supplemental request's pinned stream", exception.Message);
        Assert.Contains("Create a new supplemental request", exception.Message);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_UsesParsedEntryCountAndFallsBackFromInvalidPreferredCache()
    {
        var mediaPath = Path.Combine(_tempDirectory, "parsed-count.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(127, mediaPath);
        var cacheService = CreateCacheService();
        var invalidPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        var invalidContents = CreateCountableButInvalidSrt(80);
        await File.WriteAllTextAsync(invalidPath, invalidContents);
        cacheService.RecordSourceSnapshot(invalidPath, mediaPath);
        Assert.Equal(80, SubtitleExtractionService.CountSubtitleEntries(invalidPath));
        Assert.Empty(await new SubtitleService(NullLogger<SubtitleService>.Instance).ReadSubtitles(invalidPath));

        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 0, "eng", "English Full", invalidPath));
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English Full", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                1,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                await File.WriteAllTextAsync(outputPath, CreateSrt(80));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return outputPath;
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 228,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = invalidPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(80, prepared.EntryCount);
        Assert.Equal(invalidContents, await File.ReadAllTextAsync(invalidPath));
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            1,
            It.IsAny<string>(),
            "subrip"), Times.Once);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_PinsSupplementalRequestToItsSnapshotIdentity()
    {
        var mediaPath = Path.Combine(_tempDirectory, "pinned-supplemental.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(128, mediaPath);
        var cacheService = CreateCacheService();
        var pinnedPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        var currentPath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 1, "subrip", "eng");
        await File.WriteAllTextAsync(pinnedPath, CreateSrt(15));
        await File.WriteAllTextAsync(currentPath, CreateSrt(20));
        cacheService.RecordSourceSnapshot(pinnedPath, mediaPath);
        cacheService.RecordSourceSnapshot(currentPath, mediaPath);

        var pinnedSubtitle = CreateTextSubtitle(movie, 0, "eng", "Signs/Songs", pinnedPath);
        pinnedSubtitle.IsForced = true;
        var otherSubtitle = CreateTextSubtitle(movie, 1, "eng", "Signs/Songs", currentPath);
        otherSubtitle.IsForced = true;
        movie.EmbeddedSubtitles.Add(pinnedSubtitle);
        movie.EmbeddedSubtitles.Add(otherSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var snapshotService = CreateSnapshotService(subtitleService);
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            snapshotService);
        var request = new TranslationRequest
        {
            Id = 229,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceDedupeKey = "supplemental:signs-songs",
            SourceSubtitleType = SubtitleLanguageHelper.TypeSignsSongs,
            SubtitleToTranslate = currentPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType,
            SourceSnapshotIdentity = snapshotService
                .CreateEmbeddedSnapshot(pinnedSubtitle, "en")
                .Identity
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: true,
                SupplementalTranslationEnabled: true));

        Assert.NotNull(prepared);
        Assert.Equal(0, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(pinnedPath, prepared.Path);
        Assert.Equal(15, prepared.EntryCount);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_RejectsSupplementalManualDialogueTrack()
    {
        var mediaPath = Path.Combine(_tempDirectory, "manual-supplemental-dialogue.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(129, mediaPath);
        var cacheService = CreateCacheService();
        var sourcePath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        await File.WriteAllTextAsync(sourcePath, CreateSrt(80));
        cacheService.RecordSourceSnapshot(sourcePath, mediaPath);
        var subtitle = CreateTextSubtitle(movie, 0, "eng", "English Full", sourcePath);
        subtitle.IsForced = true;
        movie.EmbeddedSubtitles.Add(subtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var snapshotService = CreateSnapshotService(subtitleService);
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            cacheService,
            snapshotService);
        var request = new TranslationRequest
        {
            Id = 230,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceDedupeKey = "supplemental:forced",
            SourceSubtitleType = SubtitleLanguageHelper.TypeForced,
            SubtitleToTranslate = sourcePath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType,
            SourceSnapshotIdentity = snapshotService.CreateEmbeddedSnapshot(subtitle, "en").Identity
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: true,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: true,
                    SupplementalTranslationEnabled: true)));

        Assert.Contains("Manual stream 0", exception.Message);
        Assert.Contains("full-dialogue", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Create a new primary request", exception.Message);
    }

    [Theory]
    [InlineData("Signs/Songs", true)]
    [InlineData("English SDH", false)]
    public async Task PrepareTranslationSourceAsync_ManualPrimaryCannotUseSupplementalOrDisallowedCaption(
        string title,
        bool isForced)
    {
        var mediaPath = Path.Combine(_tempDirectory, $"manual-rejected-{Guid.NewGuid():N}.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(231, mediaPath);
        var cacheService = CreateCacheService();
        var sourcePath = cacheService.GetCachePath(movie.Id, MediaType.Movie, 0, "subrip", "eng");
        await File.WriteAllTextAsync(sourcePath, CreateSrt(80));
        cacheService.RecordSourceSnapshot(sourcePath, mediaPath);
        var subtitle = CreateTextSubtitle(movie, 0, "eng", title, sourcePath);
        subtitle.IsForced = isForced;
        movie.EmbeddedSubtitles.Add(subtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var resolver = CreateResolver(Mock.Of<ISubtitleExtractionService>(), cacheService);
        var request = new TranslationRequest
        {
            Id = 232,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourcePath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: true,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: false,
                    SupplementalTranslationEnabled: false)));
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_UsesCleanFullDialogueOverPathologicalAssAndCaptionFallbacks()
    {
        var mediaPath = Path.Combine(_tempDirectory, "source-fitness.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(233, mediaPath);
        var cacheService = CreateCacheService();
        var assPath = Path.Combine(_tempDirectory, "pathological.en.ass");
        await File.WriteAllTextAsync(
            assPath,
            "[Events]" + Environment.NewLine + string.Join(
                Environment.NewLine,
                Enumerable.Range(1, 600).Select(index =>
                    $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,{{\\p1}}m 0 0 l 10 10")));
        var assSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "Full Subtitles",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = assPath
        };
        var captionPath = Path.Combine(_tempDirectory, "caption.en.srt");
        var fullPath = Path.Combine(_tempDirectory, "full.en.srt");
        await File.WriteAllTextAsync(captionPath, CreateSrt(80));
        await File.WriteAllTextAsync(fullPath, CreateSrt(80));
        movie.EmbeddedSubtitles.Add(assSubtitle);
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English SDH", captionPath));
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 2, "eng", "English Full", fullPath));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var resolver = CreateResolver(Mock.Of<ISubtitleExtractionService>(), cacheService);
        var request = new TranslationRequest
        {
            Id = 234,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = assPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: true,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(2, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(fullPath, prepared.Path);
        Assert.Equal(80, prepared.EntryCount);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_PreservesCurrentPathologicalAssWithinBestAvailableRole()
    {
        var mediaPath = Path.Combine(_tempDirectory, "current-pathological.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(244, mediaPath);
        var currentPath = Path.Combine(_tempDirectory, "current.en.ass");
        var higherScorePath = Path.Combine(_tempDirectory, "higher-score.en.ass");
        var drawingEvents = "[Events]" + Environment.NewLine + string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 600).Select(index =>
                $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,{{\\p1}}m 0 0 l 10 10"));
        await File.WriteAllTextAsync(currentPath, drawingEvents);
        await File.WriteAllTextAsync(higherScorePath, drawingEvents);
        var currentSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = currentPath
        };
        var higherScoreSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            Title = "English Full Dialogue",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = higherScorePath
        };
        movie.EmbeddedSubtitles.Add(currentSubtitle);
        movie.EmbeddedSubtitles.Add(higherScoreSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var snapshotService = CreateSnapshotService(subtitleService);
        var resolver = CreateResolver(
            Mock.Of<ISubtitleExtractionService>(),
            CreateCacheService(),
            snapshotService);
        var request = new TranslationRequest
        {
            Id = 245,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = currentPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType,
            SourceSnapshotIdentity = snapshotService.CreateEmbeddedSnapshot(currentSubtitle, "en").Identity
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: true,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(0, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(currentPath, prepared.Path);
        Assert.Equal(600, prepared.EntryCount);
        Assert.Equal(
            request.SourceSnapshotIdentity,
            snapshotService.CreateEmbeddedSnapshot(prepared.SelectedSubtitle!, request.SourceLanguage).Identity);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_SkipsMalformedAssAndUsesValidAutomaticAlternative()
    {
        var mediaPath = Path.Combine(_tempDirectory, "malformed-ass-source.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(246, mediaPath);
        var malformedPath = Path.Combine(_tempDirectory, "malformed.en.ass");
        await File.WriteAllTextAsync(malformedPath, "[Events]" + Environment.NewLine + "Format: malformed");
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English Full",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = malformedPath
        });
        movie.EmbeddedSubtitles.Add(CreateTextSubtitle(movie, 1, "eng", "English Full", null));
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                1,
                It.IsAny<string>(),
                "subrip"))
            .Returns(async (string _, int _, string outputPath, string _) =>
            {
                await File.WriteAllTextAsync(outputPath, CreateSrt(80));
                return outputPath;
            });
        var parsedSubtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(It.IsAny<string>()))
            .Returns((string path) => string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(malformedPath),
                    StringComparison.OrdinalIgnoreCase)
                ? Task.FromException<List<SubtitleItem>>(
                    new IndexOutOfRangeException("Malformed ASS format declaration"))
                : parsedSubtitleService.ReadSubtitles(path));

        var resolver = CreateResolver(
            extractionService.Object,
            CreateCacheService(),
            subtitleServiceOverride: subtitleServiceMock.Object);
        var request = new TranslationRequest
        {
            Id = 247,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = malformedPath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var prepared = await resolver.PrepareTranslationSourceAsync(
            request,
            new TranslationSourcePreparationOptions(
                HasManualStreamSelection: false,
                AllowCaptionFallback: false,
                IsSupplementalRequest: false,
                SupplementalTranslationEnabled: false));

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(80, prepared.EntryCount);
        subtitleServiceMock.Verify(service => service.ReadSubtitles(malformedPath), Times.Exactly(2));
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            mediaPath,
            1,
            It.IsAny<string>(),
            "subrip"), Times.Once);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_PropagatesIoFailureWhileAssessingCurrentCandidate()
    {
        var mediaPath = Path.Combine(_tempDirectory, "io-source.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(248, mediaPath);
        var sourcePath = Path.Combine(_tempDirectory, "source.en.ass");
        await File.WriteAllTextAsync(sourcePath, "[Events]");
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "English Full",
            CodecName = "ass",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = sourcePath
        });
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(sourcePath))
            .ThrowsAsync(new IOException("Storage read failed"));
        var extractionService = new Mock<ISubtitleExtractionService>();
        var resolver = CreateResolver(
            extractionService.Object,
            CreateCacheService(),
            subtitleServiceOverride: subtitleServiceMock.Object);
        var request = new TranslationRequest
        {
            Id = 249,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = sourcePath,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = SourceSubtitleSnapshot.EmbeddedType
        };

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            resolver.PrepareTranslationSourceAsync(
                request,
                new TranslationSourcePreparationOptions(
                    HasManualStreamSelection: false,
                    AllowCaptionFallback: false,
                    IsSupplementalRequest: false,
                    SupplementalTranslationEnabled: false)));

        Assert.Equal("Storage read failed", exception.Message);
        extractionService.Verify(service => service.ExtractSubtitleToFile(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
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
        // the row because the OCR artifact is missing (IsStaleManagedArtifact) â€” the correct
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
        // fresh â€” the correct end state for a missing OCR artifact.
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

    [Fact]
    public async Task PrepareTranslationSourceAsync_SparseMatchedSignsTrack_AdoptsContradictedFullDialogueTrack()
    {
        // Live Initial D shape: request en, an en-tagged signs track with 37 cues and a full
        // English dialogue track the container mislabels jpn. Neither stream is cached on disk
        // yet, so the entry counts are unknown when selection starts. The sparse pre-check only
        // trusts cached counts, so the post-extraction contradiction fallback must recover the
        // mislabeled full dialogue stream instead of failing with "no suitable source".
        var mediaPath = Path.Combine(_tempDirectory, "initial-d-shape.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(911, mediaPath);
        var cacheService = CreateCacheService();

        var sparseSubtitle = CreateTextSubtitle(movie, 0, "eng", "Signs", null);
        var fullSubtitle = CreateTextSubtitle(movie, 1, "jpn", "Full", null);
        fullSubtitle.IsForced = true;

        movie.EmbeddedSubtitles.Add(sparseSubtitle);
        movie.EmbeddedSubtitles.Add(fullSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(37));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                1,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(274));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });

        var detector = new Mock<ISubtitleLanguageContradictionDetector>();
        detector
            .Setup(service => service.TryAdoptDetectedSourceLanguageAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<IReadOnlyList<EmbeddedSubtitle>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                fullSubtitle.DetectedLanguage = "en";
                fullSubtitle.DetectedLanguageConfidence = 0.96;
                fullSubtitle.LanguageDetectionVersion =
                    SubtitleLanguageHelper.CurrentLanguageDetectionVersion;
                return Task.FromResult<EmbeddedSubtitle?>(fullSubtitle);
            });

        var durationProvider = new Mock<IMediaDurationProvider>();
        durationProvider
            .Setup(provider => provider.GetDurationSecondsAsync(mediaPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1440);

        var resolver = CreateResolver(
            extractionService.Object,
            cacheService,
            mediaDurationProvider: durationProvider.Object,
            languageContradictionDetector: detector.Object);

        var request = new TranslationRequest
        {
            Id = 9111,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = Lingarr.Server.Models.Subtitle.SourceSubtitleSnapshot.EmbeddedType
        };
        var options = new TranslationSourcePreparationOptions(
            HasManualStreamSelection: false,
            AllowCaptionFallback: false,
            IsSupplementalRequest: false,
            SupplementalTranslationEnabled: false);

        var prepared = await resolver.PrepareTranslationSourceAsync(request, options);

        Assert.NotNull(prepared);
        Assert.Equal(1, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(274, prepared.EntryCount);
        detector.Verify(
            service => service.TryAdoptDetectedSourceLanguageAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<IReadOnlyList<EmbeddedSubtitle>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PrepareTranslationSourceAsync_AcceptsForcedTextTrackAsForcedDialogue()
    {
        // A forced-flagged track that actually contains full dialogue is promoted at queue time
        // as ForcedDialogue. The request then has to resolve that same track while translation
        // executes, otherwise the promoted request would fail with "no suitable source".
        var mediaPath = Path.Combine(_tempDirectory, "forced-dialogue-promotion.mkv");
        await File.WriteAllTextAsync(mediaPath, "media source");
        var movie = CreateMovie(912, mediaPath);
        var cacheService = CreateCacheService();

        var forcedSubtitle = CreateTextSubtitle(movie, 0, "eng", "English", null);
        forcedSubtitle.IsForced = true;

        movie.EmbeddedSubtitles.Add(forcedSubtitle);
        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var extractionService = new Mock<ISubtitleExtractionService>();
        extractionService
            .Setup(service => service.ExtractSubtitleToFile(
                mediaPath,
                0,
                It.IsAny<string>(),
                "subrip"))
            .Returns((string _, int _, string outputPath, string _) =>
            {
                File.WriteAllText(outputPath, CreateSrt(625));
                cacheService.RecordSourceSnapshot(outputPath, mediaPath);
                return Task.FromResult<string?>(outputPath);
            });

        var resolver = CreateResolver(extractionService.Object, cacheService);
        var request = new TranslationRequest
        {
            Id = 9121,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending,
            SourceSubtitleType = SubtitleLanguageHelper.TypeForcedDialogue,
            SourceSnapshotStreamIndex = 0,
            SourceSnapshotType = Lingarr.Server.Models.Subtitle.SourceSubtitleSnapshot.EmbeddedType
        };
        var options = new TranslationSourcePreparationOptions(
            HasManualStreamSelection: false,
            AllowCaptionFallback: false,
            IsSupplementalRequest: false,
            SupplementalTranslationEnabled: false);

        var prepared = await resolver.PrepareTranslationSourceAsync(request, options);

        Assert.NotNull(prepared);
        Assert.Equal(0, prepared.SelectedSubtitle?.StreamIndex);
        Assert.Equal(625, prepared.EntryCount);
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

    private Movie CreateMovie(int id, string mediaPath)
    {
        return new Movie
        {
            Id = id,
            RadarrId = id,
            Title = "Movie",
            FileName = Path.GetFileName(mediaPath),
            Path = Path.GetDirectoryName(mediaPath),
            DateAdded = DateTime.UtcNow
        };
    }

    private static EmbeddedSubtitle CreateTextSubtitle(
        Movie movie,
        int streamIndex,
        string language,
        string title,
        string? extractedPath)
    {
        return new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = streamIndex,
            Language = language,
            Title = title,
            CodecName = "subrip",
            IsTextBased = true,
            IsExtracted = extractedPath != null,
            ExtractedPath = extractedPath
        };
    }

    private EmbeddedSubtitleCacheService CreateCacheService()
    {
        return new EmbeddedSubtitleCacheService(
            NullLogger<EmbeddedSubtitleCacheService>.Instance,
            Path.Combine(_tempDirectory, "cache"),
            TimeSpan.FromDays(30));
    }

    private SourceSubtitleResolver CreateResolver(
        ISubtitleExtractionService extractionService,
        IEmbeddedSubtitleCacheService cacheService,
        ISourceSubtitleSnapshotService? snapshotService = null,
        ISubtitleOcrService? subtitleOcrService = null,
        ISubtitleService? subtitleServiceOverride = null,
        IMediaDurationProvider? mediaDurationProvider = null,
        ISubtitleLanguageContradictionDetector? languageContradictionDetector = null)
    {
        var subtitleService = subtitleServiceOverride ?? new SubtitleService(NullLogger<SubtitleService>.Instance);
        return new SourceSubtitleResolver(
            _dbContext,
            subtitleService,
            extractionService,
            snapshotService ?? Mock.Of<ISourceSubtitleSnapshotService>(),
            cacheService,
            subtitleOcrService ?? Mock.Of<ISubtitleOcrService>(),
            NullLogger<SourceSubtitleResolver>.Instance,
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance),
            mediaDurationProvider,
            languageContradictionDetector);
    }

    private SourceSubtitleSnapshotService CreateSnapshotService(ISubtitleService subtitleService)
    {
        return new SourceSubtitleSnapshotService(
            _dbContext,
            Mock.Of<ISettingService>(),
            subtitleService,
            NullLogger<SourceSubtitleSnapshotService>.Instance);
    }

    private static string CreateSrt(int entryCount)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(1, entryCount).Select(index =>
            {
                var startSeconds = index - 1;
                var endSeconds = index;
                var start = $"{startSeconds / 3600:00}:{startSeconds / 60 % 60:00}:{startSeconds % 60:00},000";
                var end = $"{endSeconds / 3600:00}:{endSeconds / 60 % 60:00}:{endSeconds % 60:00},000";
                return $"{index}{Environment.NewLine}{start} --> {end}{Environment.NewLine}Dialogue line {index}.";
            }));
    }

    private static string CreateCountableButInvalidSrt(int entryCount)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(1, entryCount).Select(index =>
                $"{index}{Environment.NewLine}not-a-time --> also-not-a-time{Environment.NewLine}Dialogue line {index}."));
    }
}
