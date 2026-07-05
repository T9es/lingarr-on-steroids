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

public class CustomMediaStateServiceTests
{
    [Fact]
    public async Task UpdateStateAsync_WithOnlyTemporaryExternalSource_ReturnsAwaitingSource()
    {
        await using var context = BuildContext();
        var item = await CreateItemAsync(context, 1);

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
                }
            });

        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        subtitleExtractionServiceMock
            .Setup(service => service.ProbeEmbeddedSubtitles(item.Path))
            .ReturnsAsync(new List<EmbeddedSubtitle>());

        var settingServiceMock = CreateSettingsMock();
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var state = await service.UpdateStateAsync(item, saveChanges: false);

        Assert.Equal(TranslationState.AwaitingSource, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithSparseEmbeddedTargetOnly_ReturnsPending()
    {
        await using var context = BuildContext();
        var item = await CreateItemAsync(context, 2);

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

        var settingServiceMock = CreateSettingsMock();
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var state = await service.UpdateStateAsync(item, saveChanges: false);

        Assert.Equal(TranslationState.Pending, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithDefaultSkipWhenTargetEmbedded_TreatsQualifiedEmbeddedTargetAsSatisfied()
    {
        await using var context = BuildContext();
        var item = await CreateItemAsync(context, 21);

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
                    Language = "pl",
                    Title = string.Empty,
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = false
                }
            });

        var settingServiceMock = CreateSettingsMock();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync((string?)null);
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var state = await service.UpdateStateAsync(item, saveChanges: false);

        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithEmbeddedTargetAndBothMode_ReturnsComplete()
    {
        await using var context = BuildContext();
        var item = await CreateItemAsync(context, 22);

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

        var settingServiceMock = CreateSettingsMock();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("both");
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();
        sourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveCurrentSnapshotAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<MediaType>(),
                It.IsAny<IReadOnlyCollection<EmbeddedSubtitle>>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSubtitleSnapshot?)null);

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var state = await service.UpdateStateAsync(item, saveChanges: false);

        Assert.Equal(TranslationState.Complete, state);
    }

    [Fact]
    public async Task UpdateStateAsync_WithStaleCompletedCustomTranslation_ReturnsStale()
    {
        await using var context = BuildContext();
        var item = await CreateItemAsync(context, 3);

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

        var settingServiceMock = CreateSettingsMock();
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

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var state = await service.UpdateStateAsync(item, saveChanges: false);

        Assert.Equal(TranslationState.Stale, state);
    }

    [Fact]
    public async Task GetItemsNeedingTranslationAsync_IncludesCompletedItemsAfterActionableWork()
    {
        await using var context = BuildContext();
        var pendingItem = await CreateItemAsync(context, 4);
        var completeItem = await CreateItemAsync(context, 5);

        pendingItem.TranslationState = TranslationState.Pending;
        pendingItem.StateSettingsVersion = 1;
        pendingItem.LastSubtitleCheckAt = DateTime.UtcNow;

        completeItem.TranslationState = TranslationState.Complete;
        completeItem.StateSettingsVersion = 1;
        completeItem.LastSubtitleCheckAt = DateTime.UtcNow.AddHours(-2);

        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        var settingServiceMock = CreateSettingsMock();
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var items = await service.GetItemsNeedingTranslationAsync(limit: 2);

        Assert.Collection(
            items,
            item => Assert.Equal(pendingItem.Id, item.Id),
            item => Assert.Equal(completeItem.Id, item.Id));
    }

    [Fact]
    public async Task GetItemsNeedingTranslationAsync_DoesNotReturnCompletedItemTwice_WhenSettingsVersionIsOutdated()
    {
        await using var context = BuildContext();
        var completeItem = await CreateItemAsync(context, 6);

        completeItem.TranslationState = TranslationState.Complete;
        completeItem.StateSettingsVersion = 0;
        completeItem.LastSubtitleCheckAt = DateTime.UtcNow.AddHours(-3);

        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        var settingServiceMock = CreateSettingsMock();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.LanguageSettingsVersion))
            .ReturnsAsync("1");
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var items = await service.GetItemsNeedingTranslationAsync(limit: 2);

        Assert.Single(items);
        Assert.Equal(completeItem.Id, items[0].Id);
    }

    [Fact]
    public async Task GetItemsNeedingTranslationAsync_ReservesCompletedRecheck_WhenActionableBacklogExists()
    {
        await using var context = BuildContext();
        var firstPendingItem = await CreateItemAsync(context, 7);
        var secondPendingItem = await CreateItemAsync(context, 8);
        var completeItem = await CreateItemAsync(context, 9);

        firstPendingItem.TranslationState = TranslationState.Pending;
        firstPendingItem.StateSettingsVersion = 1;
        firstPendingItem.LastSubtitleCheckAt = DateTime.UtcNow;

        secondPendingItem.TranslationState = TranslationState.Pending;
        secondPendingItem.StateSettingsVersion = 1;
        secondPendingItem.LastSubtitleCheckAt = DateTime.UtcNow.AddMinutes(5);

        completeItem.TranslationState = TranslationState.Complete;
        completeItem.StateSettingsVersion = 1;
        completeItem.LastSubtitleCheckAt = DateTime.UtcNow.AddHours(-4);

        await context.SaveChangesAsync();

        var subtitleServiceMock = new Mock<ISubtitleService>();
        var subtitleExtractionServiceMock = new Mock<ISubtitleExtractionService>();
        var settingServiceMock = CreateSettingsMock();
        var sourceSubtitleSnapshotServiceMock = new Mock<ISourceSubtitleSnapshotService>();

        var service = new CustomMediaStateService(
            context,
            settingServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            sourceSubtitleSnapshotServiceMock.Object,
            NullLogger<CustomMediaStateService>.Instance);

        var items = await service.GetItemsNeedingTranslationAsync(limit: 2);

        Assert.Collection(
            items,
            item => Assert.Equal(firstPendingItem.Id, item.Id),
            item => Assert.Equal(completeItem.Id, item.Id));
    }

    private static Mock<ISettingService> CreateSettingsMock()
    {
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" }
            });
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "pl", Name = "Polish" }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("match-source");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded))
            .ReturnsAsync("true");

        return settingServiceMock;
    }

    private static async Task<CustomMediaItem> CreateItemAsync(LingarrDbContext context, int id)
    {
        var source = new CustomSource
        {
            Id = id,
            Name = $"Custom Source {id}",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = @"C:\media\custom",
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var item = new CustomMediaItem
        {
            Id = id * 10,
            CustomSourceId = source.Id,
            CustomSource = source,
            ItemKind = CustomMediaItemKind.Movie,
            Title = $"Custom Movie {id}",
            FileName = "custom.movie.mkv",
            Path = @"C:\media\custom\custom.movie.mkv",
            RelativePath = "custom.movie.mkv",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(source);
        context.CustomMediaItems.Add(item);
        await context.SaveChangesAsync();
        return item;
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LingarrDbContext(options);
    }
}
