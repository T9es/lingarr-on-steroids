using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class MediaStateServiceOcrTests
{
    [Fact]
    public async Task UpdateStateAsync_WithOcrDisabledAndOnlyPgs_ReturnsAwaitingSource()
    {
        await using var context = BuildContext();
        var movie = await CreateMovieWithPgsAsync(context, SubtitleOcrStatus.NotStarted);
        var service = BuildService(context, ocrEnabled: "false");

        var state = await service.UpdateStateAsync(movie, MediaType.Movie, saveChanges: false);

        Assert.Equal(TranslationState.AwaitingSource, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithOcrEnabledAndOnlyPgs_ReturnsOcrPending()
    {
        await using var context = BuildContext();
        var movie = await CreateMovieWithPgsAsync(context, SubtitleOcrStatus.NotStarted);
        var service = BuildService(context, ocrEnabled: "true");

        var state = await service.UpdateStateAsync(movie, MediaType.Movie, saveChanges: false);

        Assert.Equal(TranslationState.OcrPending, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithBlockedOcr_ReturnsOcrBlocked()
    {
        await using var context = BuildContext();
        var movie = await CreateMovieWithPgsAsync(context, SubtitleOcrStatus.BlockedLowQuality);
        var service = BuildService(context, ocrEnabled: "true");

        var state = await service.UpdateStateAsync(movie, MediaType.Movie, saveChanges: false);

        Assert.Equal(TranslationState.OcrBlocked, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithAutoModeAndNoConfiguredSourceLanguages_ReturnsOcrPending()
    {
        await using var context = BuildContext();
        var movie = await CreateMovieWithPgsAsync(context, SubtitleOcrStatus.NotStarted);
        var service = BuildService(
            context,
            ocrEnabled: "true",
            sourceLanguageMode: "auto",
            sourceLanguages: []);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie, saveChanges: false);

        Assert.Equal(TranslationState.OcrPending, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithAutoModeAndBlockedOcrAndNoConfiguredSourceLanguages_ReturnsOcrBlocked()
    {
        await using var context = BuildContext();
        var movie = await CreateMovieWithPgsAsync(context, SubtitleOcrStatus.BlockedLowQuality);
        var service = BuildService(
            context,
            ocrEnabled: "true",
            sourceLanguageMode: "auto",
            sourceLanguages: []);

        var state = await service.UpdateStateAsync(movie, MediaType.Movie, saveChanges: false);

        Assert.Equal(TranslationState.OcrBlocked, state);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static async Task<Movie> CreateMovieWithPgsAsync(
        LingarrDbContext context,
        SubtitleOcrStatus status)
    {
        var movie = new Movie
        {
            Id = 100,
            RadarrId = 100,
            Title = "PGS Movie",
            Path = "/media/movies",
            FileName = "pgs.movie.mkv",
            DateAdded = System.DateTime.UtcNow
        };
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            StreamIndex = 0,
            Language = "eng",
            Title = "English",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = status
        });

        await context.Movies.AddAsync(movie);
        await context.SaveChangesAsync();
        return movie;
    }

    private static MediaStateService BuildService(
        LingarrDbContext context,
        string ocrEnabled,
        string sourceLanguageMode = "manual",
        IReadOnlyList<SourceLanguage>? sourceLanguages = null)
    {
        sourceLanguages ??= [new SourceLanguage { Code = "en", Name = "English" }];

        var settingService = new Mock<ISettingService>();
        settingService
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(sourceLanguages.ToList());
        settingService
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "pl", Name = "Polish" }]);
        settingService
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new SourceLanguage { Code = "pl", Name = "Polish" }]);
        settingService
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");
        settingService
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingService
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");
        settingService
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync(ocrEnabled);
        settingService
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync(sourceLanguageMode);
        settingService
            .Setup(service => service.GetSetting(SettingKeys.Translation.LanguageSettingsVersion))
            .ReturnsAsync("1");

        var subtitleService = new Mock<ISubtitleService>();
        subtitleService
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());

        var snapshotService = new Mock<ISourceSubtitleSnapshotService>();
        snapshotService
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);
        snapshotService
            .Setup(service => service.ResolveCurrentSnapshotWithAutoAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<bool>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        return new MediaStateService(
            context,
            settingService.Object,
            subtitleService.Object,
            snapshotService.Object,
            NullLogger<MediaStateService>.Instance);
    }
}
