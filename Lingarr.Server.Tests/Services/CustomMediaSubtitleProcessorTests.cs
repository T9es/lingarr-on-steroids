using System;
using System.Collections.Generic;
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

public class CustomMediaSubtitleProcessorTests
{
    [Fact]
    public async Task ProcessCustomItemForceAsync_WithActiveRequestForDifferentOutputFormat_DoesNotEnqueueTranslation()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 1,
            Name = "Anime Folder",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = 10,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Movie",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(item);
        context.TranslationRequests.Add(new TranslationRequest
        {
            Id = 1,
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            WorkloadItemKey = $"custom:{item.Id}",
            CustomMediaItemId = item.Id,
            MediaId = 0,
            MediaType = MediaType.Movie,
            Title = item.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = @"C:\media\custom\custom.movie.en.ass",
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>
            {
                new()
                {
                    Path = @"C:\media\custom\custom.movie.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>());

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync(string.Empty);
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(123);
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(item, forceProcess: true);

        Assert.Equal(0, queued);
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessCustomItemForceAsync_WithTemporaryExternalSource_PicksNextValidSourceSubtitle()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 2,
            Name = "Anime Folder",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = 20,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Movie",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(item);
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>
            {
                new()
                {
                    Path = @"C:\media\custom\lingarr_temp_source_123.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                },
                new()
                {
                    Path = @"C:\media\custom\custom.movie.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>());

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");

        TranslateAbleSubtitle? capturedRequest = null;
        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .Callback<TranslateAbleSubtitle, bool>((request, _) => capturedRequest = request)
            .ReturnsAsync(123);
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(
            item,
            forceProcess: true,
            forceTranslation: true);

        Assert.Equal(1, queued);
        Assert.NotNull(capturedRequest);
        Assert.Equal(@"C:\media\custom\custom.movie.en.ass", capturedRequest!.SubtitlePath);
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessCustomItemForceAsync_WithSparseEmbeddedTarget_DoesNotTreatTargetAsSatisfied()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 3,
            Name = "Anime Folder",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = 30,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Movie",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(item);
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>
            {
                new()
                {
                    Path = @"C:\media\custom\custom.movie.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>
            {
                new()
                {
                    StreamIndex = 0,
                    Language = "pol",
                    Title = "Signs & Songs",
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = true
                }
            });

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(321);
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(
            item,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(1, queued);
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessCustomItemForceAsync_WithEmbeddedTargetAndBothMode_DoesNotEnqueueMissingOutputFormat()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 5,
            Name = "Anime Folder",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = 50,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Movie",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(item);
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>
            {
                new()
                {
                    Path = @"C:\media\custom\custom.movie.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>
            {
                new()
                {
                    StreamIndex = 0,
                    Language = "pol",
                    Title = string.Empty,
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = false
                }
            });

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("both");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(987);
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(
            item,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(0, queued);
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessCustomItemForceAsync_WithStaleCompletedCustomTranslation_RequeuesTarget()
    {
        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 4,
            Name = "Anime Folder",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = 40,
            CustomSourceId = customSource.Id,
            CustomSource = customSource,
            ItemKind = CustomMediaItemKind.Movie,
            Title = "Custom Movie",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.CustomMediaItems.Add(item);
        context.TranslationRequests.Add(new TranslationRequest
        {
            Id = 2,
            WorkloadKind = TranslationWorkloadKind.CustomSource,
            WorkloadItemKey = $"custom:{item.Id}",
            CustomMediaItemId = item.Id,
            MediaId = 0,
            MediaType = MediaType.Movie,
            Title = item.Title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = @"C:\media\custom\custom.movie.en.ass",
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddMinutes(-5),
            IsActive = false
        });
        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>
            {
                new()
                {
                    Path = @"C:\media\custom\custom.movie.en.ass",
                    FileName = "custom.movie.en",
                    Language = "en",
                    Caption = string.Empty,
                    Format = ".ass"
                },
                new()
                {
                    Path = @"C:\media\custom\custom.movie.pl.ass",
                    FileName = "custom.movie.pl",
                    Language = "pl",
                    Caption = string.Empty,
                    Format = ".ass"
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>());

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(654);
        var currentSnapshot = new SourceSubtitleSnapshot
        {
            SourceType = SourceSubtitleSnapshot.ExternalType,
            SourceLanguage = "en",
            Identity = "external|en|c:\\media\\custom\\custom.movie.en.ass",
            Fingerprint = "NEW",
            SourcePath = @"C:\media\custom\custom.movie.en.ass"
        };
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSnapshot);
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.IsRequestStaleForSnapshot(
                It.IsAny<TranslationRequest>(),
                currentSnapshot))
            .Returns(true);

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(
            item,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(1, queued);
        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Once);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LingarrDbContext(options);
    }
}
