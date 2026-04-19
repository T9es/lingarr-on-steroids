using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class CustomMediaSubtitleProcessorTests
{
    [Fact]
    public async Task ProcessCustomItemForceAsync_WithActiveRequestForDifferentOutputFormat_EnqueuesTranslation()
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

        var processor = new CustomMediaSubtitleProcessor(
            context,
            translationRequestServiceMock.Object,
            subtitleServiceMock.Object,
            subtitleExtractionServiceMock.Object,
            settingServiceMock.Object,
            NullLogger<CustomMediaSubtitleProcessor>.Instance);

        var queued = await processor.ProcessCustomItemForceAsync(item, forceProcess: true);

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
