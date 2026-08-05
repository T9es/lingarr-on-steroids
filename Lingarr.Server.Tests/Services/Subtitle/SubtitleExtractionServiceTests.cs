using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

public class SubtitleExtractionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly SubtitleExtractionService _service;

    public SubtitleExtractionServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();

        _service = new SubtitleExtractionService(
            NullLogger<SubtitleExtractionService>.Instance,
            _dbContext,
            Mock.Of<ISettingService>(),
            Mock.Of<ISubtitleService>(),
            new EmbeddedSubtitleCacheService(NullLogger<EmbeddedSubtitleCacheService>.Instance),
            Mock.Of<ISubtitleLanguageDetectionService>());
    }

    [Fact]
    public async Task ListAvailableSubtitlesAsync_ClearsMissingExtractedMovieMetadata()
    {
        var movie = new Movie
        {
            Id = 10,
            RadarrId = 10,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = CreateMediaDirectory(),
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
            ExtractedPath = Path.Combine(movie.Path!, "missing.eng.srt")
        });

        _dbContext.Movies.Add(movie);
        await _dbContext.SaveChangesAsync();

        var response = await _service.ListAvailableSubtitlesAsync(movie.Id, MediaType.Movie);

        var subtitle = Assert.Single(response);
        Assert.False(subtitle.IsExtracted);
        Assert.Null(subtitle.ExtractedPath);

        var dbSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync(es => es.MovieId == movie.Id);
        Assert.False(dbSubtitle.IsExtracted);
        Assert.Null(dbSubtitle.ExtractedPath);
    }

    [Fact]
    public async Task ListAvailableSubtitlesAsync_ClearsMissingExtractedEpisodeMetadata()
    {
        var show = new Show
        {
            Id = 20,
            SonarrId = 20,
            Title = "Show",
            Path = CreateMediaDirectory(),
            DateAdded = DateTime.UtcNow
        };

        var season = new Season
        {
            Id = 21,
            SeasonNumber = 1,
            Path = show.Path,
            Show = show
        };

        var episode = new Episode
        {
            Id = 22,
            SonarrId = 22,
            EpisodeNumber = 1,
            Title = "Episode",
            FileName = "episode.mkv",
            Path = season.Path,
            DateAdded = DateTime.UtcNow,
            Season = season
        };

        episode.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            EpisodeId = episode.Id,
            StreamIndex = 1,
            Language = "fre",
            CodecName = "subrip",
            IsTextBased = true,
            IsExtracted = true,
            ExtractedPath = Path.Combine(episode.Path!, "missing.fre.srt")
        });

        _dbContext.Shows.Add(show);
        _dbContext.Seasons.Add(season);
        _dbContext.Episodes.Add(episode);
        await _dbContext.SaveChangesAsync();

        var response = await _service.ListAvailableSubtitlesAsync(episode.Id, MediaType.Episode);

        var subtitle = Assert.Single(response);
        Assert.False(subtitle.IsExtracted);
        Assert.Null(subtitle.ExtractedPath);

        var dbSubtitle = await _dbContext.EmbeddedSubtitles.SingleAsync(es => es.EpisodeId == episode.Id);
        Assert.False(dbSubtitle.IsExtracted);
        Assert.Null(dbSubtitle.ExtractedPath);
    }

    [Theory]
    [InlineData("ass", ".ass")]
    [InlineData("ssa", ".ssa")]
    [InlineData("subrip", ".srt")]
    public void GetExtractedSubtitlePath_UsesNativeTextSubtitleExtension(string codecName, string expectedExtension)
    {
        var method = typeof(SubtitleExtractionService).GetMethod(
            "GetExtractedSubtitlePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var outputPath = Assert.IsType<string>(method!.Invoke(null, [
            Path.GetTempPath(),
            "movie.mkv",
            codecName,
            "eng",
            0
        ]));

        Assert.EndsWith(expectedExtension, outputPath);
    }

    [Fact]
    public void GetExtractedSubtitlePath_WithStreamSpecificLanguageTag_UsesUniqueSubtitlePath()
    {
        var tagMethod = typeof(SubtitleExtractionService).GetMethod(
            "BuildStreamSpecificLanguageTag",
            BindingFlags.NonPublic | BindingFlags.Static);
        var pathMethod = typeof(SubtitleExtractionService).GetMethod(
            "GetExtractedSubtitlePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(tagMethod);
        Assert.NotNull(pathMethod);

        var languageTag = Assert.IsType<string>(tagMethod!.Invoke(null, ["eng", 3]));
        var outputPath = Assert.IsType<string>(pathMethod!.Invoke(null, [
            Path.GetTempPath(),
            "episode.mkv",
            "ass",
            languageTag,
            3
        ]));

        Assert.EndsWith(".eng.s3.ass", outputPath);
    }

    [Fact]
    public async Task EnsureExtractionMarkerAsync_PrependsMarkerToAssFiles()
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "movie.eng.ass");
        await File.WriteAllTextAsync(
            filePath,
            """
            [Script Info]
            Title: Example

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello
            """);

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.StartsWith(SubtitleExtractionService.ExtractionMarkerPrefix, lines[0]);
        Assert.Contains("[Script Info]", lines);
        Assert.Contains("Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello", lines);
    }

    [Fact]
    public async Task EnsureExtractionMarkerAsync_PreservesSrtContentAndWritesCorrectCount()
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "episode.eng.srt");
        var original =
            "1\r\n00:00:01,000 --> 00:00:02,000\r\nHello there\r\n\r\n" +
            "2\r\n00:00:03,000 --> 00:00:04,000\r\nWorld\r\n\r\n" +
            // Cue with empty text must NOT be counted as an entry
            "3\r\n00:00:05,000 --> 00:00:06,000\r\n\r\n";
        await File.WriteAllTextAsync(filePath, original);

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath);
        var header = $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=2\n\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        Assert.True(bytes.Length > headerBytes.Length, "Marked file must contain header plus content");
        Assert.True(
            bytes.AsSpan(0, headerBytes.Length).SequenceEqual(headerBytes),
            $"Marker header mismatch: {Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(headerBytes.Length, bytes.Length)))}");
        Assert.True(
            bytes.AsSpan(headerBytes.Length).SequenceEqual(Encoding.UTF8.GetBytes(original)),
            "Original content (including CRLF line endings) must be preserved byte-for-byte");
        Assert.False(File.Exists(filePath + ".lingarr-tmp"), "Temp file must be cleaned up");
    }

    [Fact]
    public async Task EnsureExtractionMarkerAsync_PreservesAssContentAndWritesCorrectCount()
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "movie.eng.ass");
        var original =
            "[Script Info]\r\n" +
            "Title: Héllo Wörld\r\n" +
            "\r\n" +
            "[Events]\r\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n" +
            "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,not a dialogue\r\n" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello\r\n" +
            "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,World\r\n";
        await File.WriteAllTextAsync(filePath, original);

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath);
        var header = $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=2\n\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        Assert.True(bytes.Length > headerBytes.Length, "Marked file must contain header plus content");
        Assert.True(
            bytes.AsSpan(0, headerBytes.Length).SequenceEqual(headerBytes),
            $"Marker header mismatch: {Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(headerBytes.Length, bytes.Length)))}");
        Assert.True(
            bytes.AsSpan(headerBytes.Length).SequenceEqual(Encoding.UTF8.GetBytes(original)),
            "Original content (including CRLF line endings and multibyte UTF-8) must be preserved byte-for-byte");
    }

    [Fact]
    public async Task EnsureExtractionMarkerAsync_SkipsAlreadyMarkedFilesWithoutRewrite()
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "movie.eng.ass");
        var alreadyMarked =
            "; Lingarr-Extracted: StreamIndex=0, Entries=1\n\n" +
            "[Script Info]\nTitle: Example\n\n[Events]\n" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello\n";
        await File.WriteAllTextAsync(filePath, alreadyMarked);
        var originalBytes = await File.ReadAllBytesAsync(filePath);
        var writtenAt = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(filePath, writtenAt);

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(filePath));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(filePath));
        Assert.False(File.Exists(filePath + ".lingarr-tmp"), "Temp file must not be created for skipped files");
    }

    [Fact]
    public async Task EnsureExtractionMarkerAsync_HandlesLargeFilesWithoutBuffering()
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "movie.large.ass");
        const int dialogueCount = 50000;
        await using (var writer = new StreamWriter(filePath))
        {
            await writer.WriteLineAsync("[Script Info]");
            await writer.WriteLineAsync("Title: Large");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("[Events]");
            await writer.WriteLineAsync(
                "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
            for (var i = 0; i < dialogueCount; i++)
            {
                await writer.WriteLineAsync(
                    $"Dialogue: 0,0:00:{i % 60:00}.00,0:00:{i % 60 + 1:00}.00,Default,,0,0,0,,Line {i}");
            }
        }

        var originalSize = new FileInfo(filePath).Length;
        Assert.True(originalSize > 2 * 1024 * 1024, $"Test file should be a few MB, was {originalSize} bytes");

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.StartsWith(SubtitleExtractionService.ExtractionMarkerPrefix, lines[0]);
        Assert.Contains($"Entries={dialogueCount}", lines[0]);

        var headerBytes = Encoding.UTF8.GetByteCount(
            $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries={dialogueCount}\n\n");
        Assert.Equal(originalSize + headerBytes, new FileInfo(filePath).Length);
        Assert.False(File.Exists(filePath + ".lingarr-tmp"), "Temp file must be cleaned up");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\n   ")]
    public async Task EnsureExtractionMarkerAsync_LeavesEmptyAndWhitespaceOnlyFilesUnmarked(string content)
    {
        var filePath = Path.Combine(CreateMediaDirectory(), "movie.eng.ass");
        await File.WriteAllTextAsync(filePath, content);

        await SubtitleExtractionService.EnsureExtractionMarkerAsync(filePath);

        Assert.Equal(content, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task DetachTrackedEmbeddedSubtitlesForMedia_RemovesStaleTrackedRowsBeforeRefresh()
    {
        var movie = new Movie
        {
            Id = 30,
            RadarrId = 30,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = CreateMediaDirectory(),
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
        await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();

        await _dbContext.EmbeddedSubtitles
            .Where(subtitle => subtitle.MovieId == movie.Id)
            .ExecuteDeleteAsync();
        SubtitleExtractionService.DetachTrackedEmbeddedSubtitlesForMedia(
            _dbContext,
            episodeId: null,
            movieId: movie.Id);

        _dbContext.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true
        });
        await _dbContext.SaveChangesAsync();
        await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();

        Assert.Single(movie.EmbeddedSubtitles);
        Assert.Single(_dbContext.ChangeTracker.Entries<EmbeddedSubtitle>());
    }

    [Fact]
    public void CopyOcrMetadataIfSameStream_DoesNotPreserveStaleProcessingState()
    {
        var existingSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Processing,
            OcrAttemptedAt = DateTime.UtcNow.AddDays(-1)
        };
        var newSubtitle = new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false
        };

        var method = typeof(SubtitleExtractionService).GetMethod(
            "CopyOcrMetadataIfSameStream",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        method.Invoke(_service, [new[] { existingSubtitle }, newSubtitle, "missing-media.mkv"]);

        Assert.Equal(SubtitleOcrStatus.NotStarted, newSubtitle.OcrStatus);
        Assert.Null(newSubtitle.OcrAttemptedAt);
    }

    [Fact]
    public async Task CopyOcrMetadataIfSameStream_DoesNotReattachStaleManagedOcrAfterMediaReplacement()
    {
        var mediaDirectory = CreateMediaDirectory();
        try
        {
            var mediaPath = Path.Combine(mediaDirectory, "episode.mkv");
            await File.WriteAllTextAsync(mediaPath, "old media snapshot");

            var cacheService = new EmbeddedSubtitleCacheService(
                NullLogger<EmbeddedSubtitleCacheService>.Instance,
                Path.Combine(mediaDirectory, "cache"),
                TimeSpan.FromDays(30));
            var ocrPath = cacheService.GetOcrCachePath(
                mediaId: 31,
                mediaType: MediaType.Movie,
                streamIndex: 0,
                language: "eng");
            await File.WriteAllTextAsync(ocrPath, "1\n00:00:01,000 --> 00:00:02,000\nOld OCR\n");
            cacheService.RecordSourceSnapshot(ocrPath, mediaPath);

            await File.WriteAllTextAsync(mediaPath, "new media snapshot");

            var service = new SubtitleExtractionService(
                NullLogger<SubtitleExtractionService>.Instance,
                _dbContext,
                Mock.Of<ISettingService>(),
                Mock.Of<ISubtitleService>(),
                cacheService,
                Mock.Of<ISubtitleLanguageDetectionService>());
            var existingSubtitle = new EmbeddedSubtitle
            {
                StreamIndex = 0,
                Language = "eng",
                Title = "English PGS",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false,
                OcrStatus = SubtitleOcrStatus.Succeeded,
                OcrExtractedPath = ocrPath
            };
            var newSubtitle = new EmbeddedSubtitle
            {
                StreamIndex = 0,
                Language = "eng",
                Title = "English PGS",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false
            };

            var method = typeof(SubtitleExtractionService).GetMethod(
                "CopyOcrMetadataIfSameStream",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method);
            method.Invoke(service, [new[] { existingSubtitle }, newSubtitle, mediaPath]);

            Assert.Equal(SubtitleOcrStatus.NotStarted, newSubtitle.OcrStatus);
            Assert.Null(newSubtitle.OcrExtractedPath);
            Assert.False(File.Exists(ocrPath));
        }
        finally
        {
            if (Directory.Exists(mediaDirectory))
            {
                Directory.Delete(mediaDirectory, recursive: true);
            }
        }
    }

    public void Dispose()
    {
        var mediaDirectories = _dbContext.Movies
            .Select(m => m.Path)
            .Concat(_dbContext.Shows.Select(s => s.Path))
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct()
            .ToList();

        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();

        foreach (var directory in mediaDirectories)
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string CreateMediaDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
