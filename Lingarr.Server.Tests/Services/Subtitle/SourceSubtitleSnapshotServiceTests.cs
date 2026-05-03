using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SourceSubtitleSnapshotServiceTests
{
    [Fact]
    public async Task GetStaleTargetLanguagesAsync_ShouldReturnTarget_WhenFingerprintChanged()
    {
        var dbContext = CreateDbContext();
        dbContext.TranslationRequests.Add(new TranslationRequest
        {
            Id = 1,
            MediaId = 100,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
            SourceSnapshotFingerprint = "OLD"
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            Identity = "external|en|/movies/movie.en.srt",
            Fingerprint = "NEW"
        };

        var stale = await service.GetStaleTargetLanguagesAsync(
            100,
            MediaType.Movie,
            ["pl"],
            currentSnapshot);

        Assert.Contains("pl", stale);
    }

    [Fact]
    public async Task GetStaleTargetLanguagesAsync_ShouldReturnEmpty_WhenFingerprintUnchanged()
    {
        var dbContext = CreateDbContext();
        dbContext.TranslationRequests.Add(new TranslationRequest
        {
            Id = 2,
            MediaId = 101,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddHours(-1),
            SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
            SourceSnapshotFingerprint = "SAME"
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            Identity = "external|en|/movies/movie.en.srt",
            Fingerprint = "SAME"
        };

        var stale = await service.GetStaleTargetLanguagesAsync(
            101,
            MediaType.Movie,
            ["pl"],
            currentSnapshot);

        Assert.Empty(stale);
    }

    [Fact]
    public async Task GetStaleTargetLanguagesAsync_IgnoresCompletedUploadRequestsWithCollidingMediaId()
    {
        var dbContext = CreateDbContext();
        dbContext.TranslationRequests.AddRange(
            new TranslationRequest
            {
                Id = 10,
                MediaId = 102,
                Title = "Movie",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Library,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-2),
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "SAME"
            },
            new TranslationRequest
            {
                Id = 11,
                MediaId = 102,
                Title = "Upload file",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Upload,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-1),
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "STALE-UPLOAD"
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            Identity = "external|en|/movies/movie.en.srt",
            Fingerprint = "SAME"
        };

        var stale = await service.GetStaleTargetLanguagesAsync(
            102,
            MediaType.Movie,
            ["pl"],
            currentSnapshot);

        Assert.Empty(stale);
    }

    [Fact]
    public async Task GetStaleTargetLanguagesAsync_ReturnsTarget_WhenRequiredAssOutputIsStaleButSrtIsFresh()
    {
        var dbContext = CreateDbContext();
        dbContext.TranslationRequests.AddRange(
            new TranslationRequest
            {
                Id = 20,
                MediaId = 103,
                Title = "Movie",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Library,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-1),
                SourceSubtitleFormat = ".ass",
                SubtitleOutputMode = SubtitleOutputMode.SrtOnly.ToSettingValue(),
                RequiredOutputFormats = ".srt",
                GeneratedOutputFormats = ".srt",
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "CURRENT"
            },
            new TranslationRequest
            {
                Id = 21,
                MediaId = 103,
                Title = "Movie",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Library,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-2),
                SourceSubtitleFormat = ".ass",
                SubtitleOutputMode = SubtitleOutputMode.AssOnly.ToSettingValue(),
                RequiredOutputFormats = ".ass",
                GeneratedOutputFormats = ".ass",
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "OLD"
            });
        await dbContext.SaveChangesAsync();

        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();
        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync(SubtitleOutputMode.Both.ToSettingValue());

        var service = new SourceSubtitleSnapshotService(
            dbContext,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            NullLogger<SourceSubtitleSnapshotService>.Instance);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            SourcePath = "/movies/movie.en.ass",
            Identity = "external|en|/movies/movie.en.ass",
            Fingerprint = "CURRENT"
        };

        var stale = await service.GetStaleTargetLanguagesAsync(
            103,
            MediaType.Movie,
            ["pl"],
            currentSnapshot);

        Assert.Contains("pl", stale);
    }

    [Fact]
    public async Task GetStaleTargetLanguagesAsync_IgnoresSupplementalCompletedRequestsWhenPrimaryIsFresh()
    {
        var dbContext = CreateDbContext();
        dbContext.TranslationRequests.AddRange(
            new TranslationRequest
            {
                Id = 30,
                MediaId = 104,
                Title = "Movie",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Library,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-2),
                SourceSubtitleFormat = ".srt",
                RequiredOutputFormats = ".srt",
                GeneratedOutputFormats = ".srt",
                SourceDedupeKey = "primary",
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "CURRENT"
            },
            new TranslationRequest
            {
                Id = 31,
                MediaId = 104,
                Title = "Movie",
                SourceLanguage = "en",
                TargetLanguage = "pl",
                MediaType = MediaType.Movie,
                WorkloadKind = TranslationWorkloadKind.Library,
                Status = TranslationStatus.Completed,
                CompletedAt = DateTime.UtcNow.AddHours(-1),
                SourceSubtitleFormat = ".srt",
                RequiredOutputFormats = ".srt",
                GeneratedOutputFormats = ".srt",
                SourceSubtitleType = SubtitleLanguageHelper.TypeForced,
                SourceDedupeKey = "supplemental:forced:stream:2",
                SourceSnapshotVersion = SourceSubtitleSnapshot.CurrentVersion,
                SourceSnapshotFingerprint = "OLD-FORCED"
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            Identity = "external|en|/movies/movie.en.srt",
            Fingerprint = "CURRENT"
        };

        var stale = await service.GetStaleTargetLanguagesAsync(
            104,
            MediaType.Movie,
            ["pl"],
            currentSnapshot);

        Assert.Empty(stale);
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldIgnoreTemporaryExternalSourceAndUseEmbedded()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();

        settingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");

        var service = new SourceSubtitleSnapshotService(
            dbContext,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            NullLogger<SourceSubtitleSnapshotService>.Instance);

        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Movie",
            Path = "/movies",
            FileName = "movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        var snapshot = await service.ResolveCurrentSnapshotAsync(
            movie,
            MediaType.Movie,
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            [
                new Subtitles
                {
                    Path = "/movies/lingarr_temp_source_123.en.srt",
                    FileName = "movie.en",
                    Language = "en",
                    Format = "srt"
                }
            ]);

        Assert.NotNull(snapshot);
        Assert.Equal(SourceSubtitleSnapshot.EmbeddedType, snapshot!.SourceType);
        Assert.Equal(0, snapshot.StreamIndex);
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldIgnoreLingarrExtractedExternalSourceAndUseEmbedded()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();
        var extractedPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.eng.srt");

        try
        {
            File.WriteAllText(
                extractedPath,
                $"{SubtitleExtractionService.ExtractionMarkerPrefix} StreamIndex=0, Entries=2{Environment.NewLine}{Environment.NewLine}" +
                "1\n00:00:01,000 --> 00:00:02,000\nHello\n\n" +
                "2\n00:00:03,000 --> 00:00:04,000\nWorld\n");

            settingServiceMock
                .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

            settingServiceMock
                .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
                .ReturnsAsync("false");

            var service = new SourceSubtitleSnapshotService(
                dbContext,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                NullLogger<SourceSubtitleSnapshotService>.Instance);

            var movie = new Movie
            {
                Id = 5,
                RadarrId = 5,
                Title = "Cars",
                Path = "/movies/Cars",
                FileName = "Cars.mkv",
                DateAdded = DateTime.UtcNow
            };

            var snapshot = await service.ResolveCurrentSnapshotAsync(
                movie,
                MediaType.Movie,
                [
                    new EmbeddedSubtitle
                    {
                        StreamIndex = 0,
                        Language = "eng",
                        Title = "English [SRT]",
                        CodecName = "subrip",
                        IsTextBased = true,
                        IsDefault = true
                    }
                ],
                [
                    new Subtitles
                    {
                        Path = extractedPath,
                        FileName = "Cars.eng",
                        Language = "en",
                        Format = ".srt"
                    }
                ]);

            Assert.NotNull(snapshot);
            Assert.Equal(SourceSubtitleSnapshot.EmbeddedType, snapshot!.SourceType);
            Assert.Equal(0, snapshot.StreamIndex);
        }
        finally
        {
            if (File.Exists(extractedPath))
            {
                File.Delete(extractedPath);
            }
        }
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldSkipTemporaryExternalSourceAndUseNextValidExternalSource()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();

        settingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

        settingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");

        var service = new SourceSubtitleSnapshotService(
            dbContext,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            NullLogger<SourceSubtitleSnapshotService>.Instance);

        var movie = new Movie
        {
            Id = 2,
            RadarrId = 2,
            Title = "Movie",
            Path = "/movies",
            FileName = "movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        var snapshot = await service.ResolveCurrentSnapshotAsync(
            movie,
            MediaType.Movie,
            [],
            [
                new Subtitles
                {
                    Path = "/movies/lingarr_temp_source_123.en.ass",
                    FileName = "movie.en",
                    Language = "en",
                    Format = ".ass"
                },
                new Subtitles
                {
                    Path = "/movies/movie.en.ass",
                    FileName = "movie.en",
                    Language = "en",
                    Format = ".ass"
                }
            ]);

        Assert.NotNull(snapshot);
        Assert.Equal(SourceSubtitleSnapshot.ExternalType, snapshot!.SourceType);
        Assert.EndsWith("movie.en.ass", snapshot.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("movie.en.ass", snapshot.Identity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lingarr_temp_source_", snapshot.Identity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldIgnoreSparseExternalSourceAndUseEmbeddedFullSource()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();
        var sparseExternalPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.en.srt");

        try
        {
            File.WriteAllText(
                sparseExternalPath,
                """
                1
                00:48:33,147 --> 00:48:36,317
                Tonight I would look upon your face!

                """);

            settingServiceMock
                .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

            settingServiceMock
                .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
                .ReturnsAsync("false");

            var service = new SourceSubtitleSnapshotService(
                dbContext,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                NullLogger<SourceSubtitleSnapshotService>.Instance);

            var movie = new Movie
            {
                Id = 4,
                RadarrId = 4,
                Title = "Movie",
                Path = "/movies",
                FileName = "movie.mkv",
                DateAdded = DateTime.UtcNow
            };

            var snapshot = await service.ResolveCurrentSnapshotAsync(
                movie,
                MediaType.Movie,
                [
                    new EmbeddedSubtitle
                    {
                        StreamIndex = 2,
                        Language = "eng",
                        CodecName = "subrip",
                        IsTextBased = true
                    }
                ],
                [
                    new Subtitles
                    {
                        Path = sparseExternalPath,
                        FileName = "movie.en",
                        Language = "en",
                        Format = ".srt"
                    }
                ]);

            Assert.NotNull(snapshot);
            Assert.Equal(SourceSubtitleSnapshot.EmbeddedType, snapshot!.SourceType);
            Assert.Equal(2, snapshot.StreamIndex);
        }
        finally
        {
            if (File.Exists(sparseExternalPath))
            {
                File.Delete(sparseExternalPath);
            }
        }
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldRejectPathologicalExternalAssAndUseEmbeddedSource()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();
        var pathologicalAssPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.en.ass");

        try
        {
            await File.WriteAllTextAsync(
                pathologicalAssPath,
                "[Script Info]\n" +
                "Title: CR English (US)\n\n" +
                "[Events]\n" +
                "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
                string.Join(
                    "\n",
                    Enumerable.Range(1, 600).Select(index =>
                        $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,a")));

            settingServiceMock
                .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

            settingServiceMock
                .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
                .ReturnsAsync("false");

            var service = new SourceSubtitleSnapshotService(
                dbContext,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                NullLogger<SourceSubtitleSnapshotService>.Instance);

            var movie = new Movie
            {
                Id = 6,
                RadarrId = 6,
                Title = "Movie",
                Path = "/movies",
                FileName = "movie.mkv",
                DateAdded = DateTime.UtcNow
            };

            var snapshot = await service.ResolveCurrentSnapshotAsync(
                movie,
                MediaType.Movie,
                [
                    new EmbeddedSubtitle
                    {
                        StreamIndex = 4,
                        Language = "eng",
                        Title = "English",
                        CodecName = "subrip",
                        IsTextBased = true
                    }
                ],
                [
                    new Subtitles
                    {
                        Path = pathologicalAssPath,
                        FileName = "movie.en",
                        Language = "en",
                        Format = ".ass"
                    }
                ]);

            Assert.NotNull(snapshot);
            Assert.Equal(SourceSubtitleSnapshot.EmbeddedType, snapshot!.SourceType);
            Assert.Equal(4, snapshot.StreamIndex);
            Assert.DoesNotContain(Path.GetFileName(pathologicalAssPath), snapshot.Identity, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(pathologicalAssPath))
            {
                File.Delete(pathologicalAssPath);
            }
        }
    }

    [Fact]
    public void CreateExternalSnapshot_ShouldUseContentFingerprint_WhenMetadataIsUnchanged()
    {
        var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.en.srt");
        var fixedTimestamp = new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc);

        try
        {
            File.WriteAllText(tempPath, "AAAA");
            File.SetLastWriteTimeUtc(tempPath, fixedTimestamp);
            var before = service.CreateExternalSnapshot(tempPath, "en");

            File.WriteAllText(tempPath, "BBBB");
            File.SetLastWriteTimeUtc(tempPath, fixedTimestamp);
            var after = service.CreateExternalSnapshot(tempPath, "en");

            Assert.Equal(before.FileSizeBytes, after.FileSizeBytes);
            Assert.Equal(before.LastWriteUtc, after.LastWriteUtc);
            Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ResolveCurrentSnapshotAsync_ShouldPreferLessPathologicalExtractedEmbeddedTrack()
    {
        var dbContext = CreateDbContext();
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();
        var firstPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.eng.ass");
        var secondPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.eng.ass");

        try
        {
            await File.WriteAllTextAsync(
                firstPath,
                "[Events]\n" + string.Join(
                    "\n",
                    Enumerable.Range(1, 300).Select(index =>
                        $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,{{\\an7}}Fran")));
            await File.WriteAllTextAsync(
                secondPath,
                "[Events]\n" + string.Join(
                    "\n",
                    Enumerable.Range(1, 300).Select(index =>
                        $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,Meaningful line {index}")));

            settingServiceMock
                .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
                .ReturnsAsync([new SourceLanguage { Name = "English", Code = "en" }]);

            settingServiceMock
                .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
                .ReturnsAsync("false");

            subtitleServiceMock
                .Setup(service => service.ReadSubtitles(firstPath))
                .ReturnsAsync(Enumerable.Range(1, 300)
                    .Select(index => new SubtitleItem
                    {
                        Position = index,
                        Lines = ["{\\an7}Fran"],
                        PlaintextLines = ["Fran"]
                    })
                    .ToList());

            subtitleServiceMock
                .Setup(service => service.ReadSubtitles(secondPath))
                .ReturnsAsync(Enumerable.Range(1, 300)
                    .Select(index => new SubtitleItem
                    {
                        Position = index,
                        Lines = [$"Meaningful line {index}"],
                        PlaintextLines = [$"Meaningful line {index}"]
                    })
                    .ToList());

            var service = new SourceSubtitleSnapshotService(
                dbContext,
                settingServiceMock.Object,
                subtitleServiceMock.Object,
                NullLogger<SourceSubtitleSnapshotService>.Instance);

            var movie = new Movie
            {
                Id = 3,
                RadarrId = 3,
                Title = "Movie",
                Path = "/movies",
                FileName = "movie.mkv",
                DateAdded = DateTime.UtcNow
            };

            var snapshot = await service.ResolveCurrentSnapshotAsync(
                movie,
                MediaType.Movie,
                [
                    new EmbeddedSubtitle
                    {
                        StreamIndex = 0,
                        Language = "eng",
                        Title = "Full Subtitles",
                        CodecName = "ass",
                        IsTextBased = true,
                        ExtractedPath = firstPath
                    },
                    new EmbeddedSubtitle
                    {
                        StreamIndex = 1,
                        Language = "eng",
                        Title = "Full Subtitles",
                        CodecName = "ass",
                        IsTextBased = true,
                        ExtractedPath = secondPath
                    }
                ]);

            Assert.NotNull(snapshot);
            Assert.Equal(SourceSubtitleSnapshot.EmbeddedType, snapshot!.SourceType);
            Assert.Equal(1, snapshot.StreamIndex);
        }
        finally
        {
            if (File.Exists(firstPath))
            {
                File.Delete(firstPath);
            }

            if (File.Exists(secondPath))
            {
                File.Delete(secondPath);
            }
        }
    }

    private static LingarrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LingarrDbContext(options);
    }

    private static SourceSubtitleSnapshotService CreateService(LingarrDbContext dbContext)
    {
        var settingServiceMock = new Mock<ISettingService>();
        var subtitleServiceMock = new Mock<ISubtitleService>();

        return new SourceSubtitleSnapshotService(
            dbContext,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            NullLogger<SourceSubtitleSnapshotService>.Instance);
    }
}
