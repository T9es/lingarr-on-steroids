using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Subtitle;
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
    private readonly Mock<ISourceSubtitleSnapshotService> _sourceSubtitleSnapshotServiceMock;
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
        _sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Models.FileSystem.Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.GetStaleTargetLanguagesAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<SourceSubtitleSnapshot?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task HasActiveTranslationRequestAsync_IgnoresUploadRequestsWithCollidingMediaId()
    {
        _context.TranslationRequests.Add(new TranslationRequest
        {
            Id = 900,
            MediaId = 77,
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Upload,
            Title = "Upload active",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Pending
        });
        await _context.SaveChangesAsync();

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var hasActive = await service.HasActiveTranslationRequestAsync(77, MediaType.Movie);

        Assert.False(hasActive);
    }

    [Fact]
    public async Task HasFailedTranslationRequestAsync_IgnoresUploadRequestsWithCollidingMediaId()
    {
        _context.TranslationRequests.Add(new TranslationRequest
        {
            Id = 901,
            MediaId = 78,
            MediaType = MediaType.Movie,
            WorkloadKind = TranslationWorkloadKind.Upload,
            Title = "Upload failed",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Failed
        });
        await _context.SaveChangesAsync();

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var hasFailed = await service.HasFailedTranslationRequestAsync(78, MediaType.Movie);

        Assert.False(hasFailed);
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
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Complete because embedded Dutch subtitle satisfies target
        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnPending_WhenOnlySparseEmbeddedTargetExists()
    {
        var movie = new Movie
        {
            Id = 11,
            RadarrId = 11,
            Title = "Sparse Embedded Target",
            Path = "/movies/sparse-target.mkv",
            FileName = "sparse-target.mkv",
            DateAdded = DateTime.UtcNow
        };

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = 11,
            Language = "eng",
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        });
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = 11,
            Language = "dut",
            Title = "Signs & Songs",
            IsTextBased = true,
            IsForced = true,
            CodecName = "ass",
            StreamIndex = 1
        });

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "English", Code = "en" } })
            .ReturnsAsync(new List<SourceLanguage> { new() { Name = "Dutch", Code = "nl" } });
        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Models.FileSystem.Subtitles>());

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.Pending, state);
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
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Pending because Dutch target is missing
        Assert.Equal(TranslationState.Pending, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnPending_WhenExternalTargetIsSparseEvenWithEmbeddedSource()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var sparseEnglishPath = Path.Combine(tempDirectory, "got.s01e02.en.srt");
        var sparsePolishPath = Path.Combine(tempDirectory, "got.s01e02.pl.srt");

        try
        {
            File.WriteAllText(
                sparseEnglishPath,
                """
                1
                00:48:33,147 --> 00:48:36,317
                Tonight I would look upon your face!

                """);
            File.WriteAllText(
                sparsePolishPath,
                """
                1
                00:48:33,147 --> 00:48:36,317
                Dzisiaj chce ujrzec twoja twarz!

                """);

            var movie = new Movie
            {
                Id = 22,
                RadarrId = 22,
                Title = "Game of Thrones - The Kingsroad",
                Path = tempDirectory,
                FileName = "got.s01e02.mkv",
                DateAdded = DateTime.UtcNow
            };
            movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
            {
                MovieId = 22,
                Language = "eng",
                IsTextBased = true,
                CodecName = "subrip",
                StreamIndex = 2
            });

            _context.Movies.Add(movie);
            await _context.SaveChangesAsync();

            _settingServiceMock
                .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
                .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

            _subtitleServiceMock
                .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
                .ReturnsAsync([
                    new Models.FileSystem.Subtitles
                    {
                        FileName = "got.s01e02.en.srt",
                        Language = "en",
                        Path = sparseEnglishPath,
                        Format = ".srt"
                    },
                    new Models.FileSystem.Subtitles
                    {
                        FileName = "got.s01e02.pl.srt",
                        Language = "pl",
                        Path = sparsePolishPath,
                        Format = ".srt"
                    }
                ]);

            var service = new MediaStateService(
                _context,
                _settingServiceMock.Object,
                _subtitleServiceMock.Object,
                _sourceSubtitleSnapshotServiceMock.Object,
                NullLogger<MediaStateService>.Instance);

            var state = await service.UpdateStateAsync(movie, MediaType.Movie);

            Assert.Equal(TranslationState.Pending, state);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        // Act
        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        // Assert - Should be Complete because both Dutch (external) and German (embedded) are satisfied
        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnStale_WhenTargetSnapshotIsOutdated()
    {
        var movie = new Movie
        {
            Id = 20,
            RadarrId = 20,
            Title = "Stale Snapshot Movie",
            Path = "/movies/stale",
            FileName = "stale.mkv",
            DateAdded = DateTime.UtcNow
        };

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = 20,
            Language = "eng",
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        });

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles { FileName = "stale.en", Language = "en", Path = "/movies/stale/stale.en.srt" },
                new Models.FileSystem.Subtitles { FileName = "stale.pl", Language = "pl", Path = "/movies/stale/stale.pl.srt" }
            ]);

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                MediaType.Movie,
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Models.FileSystem.Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceSubtitleSnapshot
            {
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external|en|/movies/stale/stale.en.srt",
                Fingerprint = "NEW"
            });

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.GetStaleTargetLanguagesAsync(
                movie.Id,
                MediaType.Movie,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<SourceSubtitleSnapshot?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "pl" });

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.Stale, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldRemainComplete_WhenTargetSnapshotMatches()
    {
        var movie = new Movie
        {
            Id = 21,
            RadarrId = 21,
            Title = "Fresh Snapshot Movie",
            Path = "/movies/fresh",
            FileName = "fresh.mkv",
            DateAdded = DateTime.UtcNow
        };

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = 21,
            Language = "eng",
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        });

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles { FileName = "fresh.en", Language = "en", Path = "/movies/fresh/fresh.en.srt" },
                new Models.FileSystem.Subtitles { FileName = "fresh.pl", Language = "pl", Path = "/movies/fresh/fresh.pl.srt" }
            ]);

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                MediaType.Movie,
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Models.FileSystem.Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceSubtitleSnapshot
            {
                SourceType = SourceSubtitleSnapshot.ExternalType,
                SourceLanguage = "en",
                Identity = "external|en|/movies/fresh/fresh.en.srt",
                Fingerprint = "SAME"
            });

        _sourceSubtitleSnapshotServiceMock
            .Setup(s => s.GetStaleTargetLanguagesAsync(
                movie.Id,
                MediaType.Movie,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<SourceSubtitleSnapshot?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

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
            _sourceSubtitleSnapshotServiceMock.Object,
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
            _sourceSubtitleSnapshotServiceMock.Object,
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

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnPending_WhenBothOutputsAreRequiredButOnlyOneExists()
    {
        var movie = new Movie
        {
            Id = 30,
            RadarrId = 30,
            Title = "ASS Output Movie",
            Path = "/movies/ass-output",
            FileName = "ass-output.mkv",
            DateAdded = DateTime.UtcNow
        };

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(SubtitleOutputMode.Both.ToSettingValue());

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles { FileName = "ass-output.en", Language = "en", Path = "/movies/ass-output/ass-output.en.ass", Format = ".ass" },
                new Models.FileSystem.Subtitles { FileName = "ass-output.pl", Language = "pl", Path = "/movies/ass-output/ass-output.pl.srt", Format = ".srt" }
            ]);

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.Pending, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnComplete_WhenBothOutputsAreRequiredAndBothExist()
    {
        var movie = new Movie
        {
            Id = 31,
            RadarrId = 31,
            Title = "ASS Output Complete Movie",
            Path = "/movies/ass-output-complete",
            FileName = "ass-output-complete.mkv",
            DateAdded = DateTime.UtcNow
        };

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(SubtitleOutputMode.Both.ToSettingValue());

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles { FileName = "ass-output-complete.en", Language = "en", Path = "/movies/ass-output-complete/ass-output-complete.en.ass", Format = ".ass" },
                new Models.FileSystem.Subtitles { FileName = "ass-output-complete.pl", Language = "pl", Path = "/movies/ass-output-complete/ass-output-complete.pl.srt", Format = ".srt" },
                new Models.FileSystem.Subtitles { FileName = "ass-output-complete.pl", Language = "pl", Path = "/movies/ass-output-complete/ass-output-complete.pl.ass", Format = ".ass" }
            ]);

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldIgnoreTemporaryExternalAssSource_WhenResolvingRequiredFormats()
    {
        var movie = new Movie
        {
            Id = 32,
            RadarrId = 32,
            Title = "Temporary Source Movie",
            Path = "/movies/temp-source",
            FileName = "temp-source.mkv",
            DateAdded = DateTime.UtcNow
        };

        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = 32,
            Language = "eng",
            IsTextBased = true,
            CodecName = "subrip",
            StreamIndex = 0
        });

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(SubtitleOutputMode.Both.ToSettingValue());

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles
                {
                    FileName = "lingarr_temp_source_123.en",
                    Language = "en",
                    Path = "/movies/temp-source/lingarr_temp_source_123.en.ass",
                    Format = ".ass"
                },
                new Models.FileSystem.Subtitles
                {
                    FileName = "temp-source.pl",
                    Language = "pl",
                    Path = "/movies/temp-source/temp-source.pl.srt",
                    Format = ".srt"
                }
            ]);

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task ComputeStateAsync_ShouldReturnAwaitingSource_WhenOnlyTemporaryExternalSourceExists()
    {
        var movie = new Movie
        {
            Id = 33,
            RadarrId = 33,
            Title = "Only Temporary Source Movie",
            Path = "/movies/only-temp-source",
            FileName = "only-temp-source.mkv",
            DateAdded = DateTime.UtcNow
        };

        _context.Movies.Add(movie);
        await _context.SaveChangesAsync();

        _settingServiceMock
            .SetupSequence(s => s.GetSettingAsJson<SourceLanguage>(It.IsAny<string>()))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }])
            .ReturnsAsync([new SourceLanguage { Name = "Polish", Code = "pl" }]);

        _settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(SubtitleOutputMode.Both.ToSettingValue());

        _subtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync([
                new Models.FileSystem.Subtitles
                {
                    FileName = "lingarr_temp_source_123.en",
                    Language = "en",
                    Path = "/movies/only-temp-source/lingarr_temp_source_123.en.ass",
                    Format = ".ass"
                }
            ]);

        var service = new MediaStateService(
            _context,
            _settingServiceMock.Object,
            _subtitleServiceMock.Object,
            _sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<MediaStateService>.Instance);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie);

        Assert.Equal(TranslationState.AwaitingSource, state);
    }
}
