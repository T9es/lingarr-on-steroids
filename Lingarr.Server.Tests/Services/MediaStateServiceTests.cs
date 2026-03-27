using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Microsoft.Data.Sqlite;

namespace Lingarr.Server.Tests.Services;

public class MediaStateServiceTests : IDisposable
{
    private readonly LingarrDbContext _context;
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly Mock<ISubtitleService> _subtitleServiceMock;
    private readonly DbConnection _connection;

    public MediaStateServiceTests()
    {
        // Use SQLite in-memory for tests that need ExecuteUpdateAsync support
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new LingarrDbContext(options);
        _context.Database.EnsureCreated();
        
        _settingServiceMock = new Mock<ISettingService>();
        _subtitleServiceMock = new Mock<ISubtitleService>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldDetectEmbeddedTargetLanguageSubtitles()
    {
        // Arrange - Movie with embedded English (source) and Dutch (target) subtitles
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Test Movie",
            Path = "/movies/test.mkv",
            FileName = "test.mkv",
            DateAdded = DateTime.UtcNow
        };

        // Add embedded English subtitle (source)
        var embeddedEnglish = new EmbeddedSubtitle
        {
            MovieId = 1,
            Language = "eng", // English (source)
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        };

        // Add embedded Dutch subtitle (target)
        var embeddedDutch = new EmbeddedSubtitle
        {
            MovieId = 1,
            Language = "dut", // Dutch (target)
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 1
        };

        movie.EmbeddedSubtitles.Add(embeddedEnglish);
        movie.EmbeddedSubtitles.Add(embeddedDutch);
        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        // Configure source as English, target as Dutch
        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "English", Code = "en" } }) // Source
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "Dutch", Code = "nl" } }); // Target

        // No external subtitles
        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Models.FileSystem.Subtitles>());

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Complete because embedded Dutch subtitle satisfies target
        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnPending_WhenEmbeddedTargetLanguageMissing()
    {
        // Arrange - Movie with embedded English subtitle (source), but no Dutch (target)
        var movie = new Movie
        {
            Id = 2,
            RadarrId = 2,
            Title = "Test Movie 2",
            Path = "/movies/test2.mkv",
            FileName = "test2.mkv",
            DateAdded = DateTime.UtcNow
        };

        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = 2,
            Language = "eng", // English (source)
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        // Configure source as English, target as Dutch
        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "English", Code = "en" } }) // Source
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "Dutch", Code = "nl" } }); // Target

        // No external subtitles
        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Models.FileSystem.Subtitles>());

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Pending because Dutch target is missing
        Assert.Equal(TranslationState.Pending, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldHandleBothExternalAndEmbeddedTargetLanguages()
    {
        // Arrange - Movie with embedded English (source), German (target) and external Dutch (target)
        var movie = new Movie
        {
            Id = 3,
            RadarrId = 3,
            Title = "Test Movie 3",
            Path = "/movies/test3.mkv",
            FileName = "test3.mkv",
            DateAdded = DateTime.UtcNow
        };

        // Add embedded English subtitle (source)
        var embeddedEnglish = new EmbeddedSubtitle
        {
            MovieId = 3,
            Language = "eng", // English (source)
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        };

        // Add embedded German subtitle (target)
        var embeddedGerman = new EmbeddedSubtitle
        {
            MovieId = 3,
            Language = "deu", // German (target)
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 1
        };

        movie.EmbeddedSubtitles.Add(embeddedEnglish);
        movie.EmbeddedSubtitles.Add(embeddedGerman);
        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        // Source: English, Targets: Dutch AND German
        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "English", Code = "en" } }) // Source
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "Dutch", Code = "nl" }, new() { Name = "German", Code = "de" } }); // Targets

        // External subtitle: Dutch
        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Models.FileSystem.Subtitles>
            {
                new() { FileName = "test3.nl.srt", Language = "nl" }
            });

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Complete because both Dutch (external) and German (embedded) are satisfied
        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task MarkAllStaleAsync_ShouldPreserveCompleteItems()
    {
        // Arrange
        var movie1 = new Movie
        {
            Id = 10,
            RadarrId = 10,
            Title = "Complete Movie",
            Path = "/movies/complete.mkv",
            FileName = "complete.mkv",
            TranslationState = TranslationState.Complete,
            DateAdded = DateTime.UtcNow
        };

        var movie2 = new Movie
        {
            Id = 11,
            RadarrId = 11,
            Title = "Pending Movie",
            Path = "/movies/pending.mkv",
            FileName = "pending.mkv",
            TranslationState = TranslationState.Pending,
            DateAdded = DateTime.UtcNow
        };

        var movie3 = new Movie
        {
            Id = 12,
            RadarrId = 12,
            Title = "NotApplicable Movie",
            Path = "/movies/na.mkv",
            FileName = "na.mkv",
            TranslationState = TranslationState.NotApplicable,
            DateAdded = DateTime.UtcNow
        };

        _context.Movies.AddRange(movie1, movie2, movie3);
        await _context.SaveChangesAsync();

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        await service.MarkAllStaleAsync();

        // Assert - Use AsNoTracking to bypass change tracker since ExecuteUpdateAsync bypasses it
        var completeMovie = await _context.Movies.AsNoTracking().FirstAsync(m => m.Id == 10);
        var pendingMovie = await _context.Movies.AsNoTracking().FirstAsync(m => m.Id == 11);
        var naMovie = await _context.Movies.AsNoTracking().FirstAsync(m => m.Id == 12);

        // Complete should be preserved
        Assert.Equal(TranslationState.Complete, completeMovie.TranslationState);
        
        // Pending should be marked as Stale
        Assert.Equal(TranslationState.Stale, pendingMovie.TranslationState);
        
        // NotApplicable should remain unchanged
        Assert.Equal(TranslationState.NotApplicable, naMovie.TranslationState);
    }

    [Fact]
    public async Task MarkAllStaleAsync_ShouldHandleEpisodes()
    {
        // Arrange - Use unique high IDs to avoid conflicts with other tests
        var show = new Show
        {
            Id = 9999,
            SonarrId = 9999,
            Title = "Test Show For Episode Stale Test",
            Path = "/shows/test-stale",
            DateAdded = DateTime.UtcNow
        };

        var season = new Season
        {
            Id = 9999,
            SeasonNumber = 1,
            ShowId = 9999,
            Show = show
        };

        var episode1 = new Episode
        {
            Id = 9998,
            SonarrId = 9998,
            EpisodeNumber = 1,
            Title = "Complete Episode For Stale Test",
            TranslationState = TranslationState.Complete,
            SeasonId = 9999,
            Season = season
        };

        var episode2 = new Episode
        {
            Id = 9997,
            SonarrId = 9997,
            EpisodeNumber = 2,
            Title = "InProgress Episode For Stale Test",
            TranslationState = TranslationState.InProgress,
            SeasonId = 9999,
            Season = season
        };

        // Add all entities explicitly
        _context.Shows.Add(show);
        _context.Seasons.Add(season);
        _context.Episodes.AddRange(episode1, episode2);
        await _context.SaveChangesAsync();

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        await service.MarkAllStaleAsync();

        // Assert - Use AsNoTracking to bypass change tracker since ExecuteUpdateAsync bypasses it
        var completeEpisode = await _context.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == 9998);
        var inProgressEpisode = await _context.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == 9997);

        // Complete should be preserved
        Assert.NotNull(completeEpisode);
        Assert.Equal(TranslationState.Complete, completeEpisode.TranslationState);
        
        // InProgress should be marked as Stale
        Assert.NotNull(inProgressEpisode);
        Assert.Equal(TranslationState.Stale, inProgressEpisode.TranslationState);
    }
}
