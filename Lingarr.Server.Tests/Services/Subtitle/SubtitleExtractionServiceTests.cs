using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
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
            Mock.Of<ISubtitleService>());
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
