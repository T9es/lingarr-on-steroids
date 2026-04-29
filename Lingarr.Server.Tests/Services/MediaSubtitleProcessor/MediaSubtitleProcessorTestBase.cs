using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

/// <summary>
/// Base test class providing common setup for MediaSubtitleProcessor tests.
/// </summary>
public abstract class MediaSubtitleProcessorTestBase : IDisposable
{
    protected readonly Mock<ITranslationRequestService> TranslationRequestServiceMock;
    protected readonly Mock<ILogger<IMediaSubtitleProcessor>> LoggerMock;
    protected readonly Mock<ISubtitleService> SubtitleServiceMock;
    protected readonly Mock<ISettingService> SettingServiceMock;
    protected readonly Mock<ISubtitleExtractionService> SubtitleExtractionServiceMock;
    protected readonly Mock<ISubtitleIntegrityService> SubtitleIntegrityServiceMock;
    protected readonly Mock<ISourceSubtitleSnapshotService> SourceSubtitleSnapshotServiceMock;
    protected readonly LingarrDbContext DbContext;
    protected readonly Lingarr.Server.Services.MediaSubtitleProcessor Processor;

    protected MediaSubtitleProcessorTestBase()
    {
        TranslationRequestServiceMock = new Mock<ITranslationRequestService>();
        LoggerMock = new Mock<ILogger<IMediaSubtitleProcessor>>();
        SubtitleServiceMock = new Mock<ISubtitleService>();
        SettingServiceMock = new Mock<ISettingService>();
        SubtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        SubtitleIntegrityServiceMock = new Mock<ISubtitleIntegrityService>();
        SourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        
        // Default behavior: integrity validation returns true (valid)
        SubtitleIntegrityServiceMock
            .Setup(s => s.ValidateIntegrityAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        SubtitleIntegrityServiceMock
            .Setup(s => s.ValidateIntegrityDetailedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SubtitleIntegrityCheckResult
            {
                IsValid = true,
                Reason = "valid"
            });

        SourceSubtitleSnapshotServiceMock
            .Setup(s => s.ResolveExternalSourceAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                Lingarr.Core.Interfaces.IMedia media,
                IReadOnlyCollection<Subtitles>? subtitles,
                CancellationToken _) =>
            {
                var configuredSourceLanguages = await SettingServiceMock.Object
                    .GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
                var sourceLanguage = configuredSourceLanguages
                    .Select(language => language.Code)
                    .FirstOrDefault(code => subtitles?.Any(subtitle =>
                        SubtitleLanguageHelper.LanguageMatches(subtitle.Language, code)) == true);

                if (string.IsNullOrWhiteSpace(sourceLanguage))
                {
                    return null;
                }

                var ignoreCaptions = string.Equals(
                    await SettingServiceMock.Object.GetSetting(SettingKeys.Translation.IgnoreCaptions),
                    "true",
                    StringComparison.OrdinalIgnoreCase);

                var subtitle = ignoreCaptions
                    ? subtitles?.FirstOrDefault(s =>
                          SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage)
                          && string.IsNullOrWhiteSpace(s.Caption))
                      ?? subtitles?.FirstOrDefault(s =>
                          SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage))
                    : subtitles?.FirstOrDefault(s =>
                        SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage));

                if (subtitle == null)
                {
                    return null;
                }

                return new ResolvedExternalSourceSubtitle
                {
                    Subtitle = subtitle,
                    SourceLanguage = sourceLanguage,
                    Snapshot = new SourceSubtitleSnapshot
                    {
                        SourceType = SourceSubtitleSnapshot.ExternalType,
                        SourceLanguage = sourceLanguage,
                        SourcePath = subtitle.Path,
                        Identity = $"external|{sourceLanguage}|{subtitle.Path}",
                        Fingerprint = $"fp:{subtitle.Path}"
                    }
                };
            });

        SourceSubtitleSnapshotServiceMock
            .Setup(s => s.GetStaleTargetLanguagesAsync(
                It.IsAny<int>(),
                It.IsAny<Lingarr.Core.Enum.MediaType>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Lingarr.Server.Models.Subtitle.SourceSubtitleSnapshot?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        SourceSubtitleSnapshotServiceMock
            .Setup(s => s.CreateEmbeddedSnapshot(
                It.IsAny<EmbeddedSubtitle>(),
                It.IsAny<string>()))
            .Returns((EmbeddedSubtitle subtitle, string sourceLanguage) => new SourceSubtitleSnapshot
            {
                SourceType = SourceSubtitleSnapshot.EmbeddedType,
                SourceLanguage = sourceLanguage,
                Identity = $"embedded|{sourceLanguage}|stream:{subtitle.StreamIndex}",
                Fingerprint = $"fp:embedded:{sourceLanguage}:{subtitle.StreamIndex}",
                StreamIndex = subtitle.StreamIndex
            });

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options;
        DbContext = new LingarrDbContext(options);

        Processor = new Lingarr.Server.Services.MediaSubtitleProcessor(
            TranslationRequestServiceMock.Object,
            LoggerMock.Object,
            SettingServiceMock.Object,
            SubtitleServiceMock.Object,
            SubtitleExtractionServiceMock.Object,
            SubtitleIntegrityServiceMock.Object,
            SourceSubtitleSnapshotServiceMock.Object,
            DbContext);
    }


    protected async Task<Movie> CreateTestMovie(string fileName = "test.movie")
    {
        var movie = new Movie
        {
            Id = 1,
            RadarrId = 1,
            Title = "Test Movie",
            Path = "/movies/test",
            FileName = fileName,
            MediaHash = null,
            DateAdded = System.DateTime.UtcNow
        };
        await DbContext.Movies.AddAsync(movie);
        await DbContext.SaveChangesAsync();
        return movie;
    }

    protected void SetupStandardSettings(string ignoreCaptions = "true")
    {
        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage> { new() { Code = "en", Name = "English" } });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage> { new() { Code = "ro", Name = "Romanian" } });

        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync(ignoreCaptions);
    }

    public void Dispose()
    {
        DbContext?.Dispose();
        GC.SuppressFinalize(this);
    }
}
