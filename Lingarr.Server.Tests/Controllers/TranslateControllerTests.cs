using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class TranslateControllerTests
{
    [Fact]
    public async Task QueueWithSubtitle_SkipsWhenActiveRequestUsesDifferentRequiredOutputFormats()
    {
        await using var context = BuildContext();
        context.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = 42,
            MediaType = MediaType.Movie,
            WorkloadItemKey = "library:Movie:42",
            Title = "Movie 42",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            RequiredOutputFormats = ".ass",
            Status = TranslationStatus.Pending,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true))
            .ReturnsAsync(123);

        var extractionServiceMock = new Mock<ISubtitleExtractionService>();
        extractionServiceMock
            .Setup(service => service.ListAvailableSubtitlesAsync(42, MediaType.Movie))
            .ReturnsAsync(new List<AvailableSubtitleResponse>
            {
                new()
                {
                    StreamIndex = 3,
                    CodecName = "ass",
                    IsTextBased = true
                }
            });

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new()
                {
                    Name = "Polish",
                    Code = "pl"
                }
            });
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");
        settingServiceMock
            .Setup(service => service.SetSetting(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var controller = CreateController(
            context,
            translationRequestServiceMock.Object,
            extractionServiceMock.Object,
            settingServiceMock.Object);

        var response = await controller.QueueWithSubtitle(new QueueWithSubtitleRequest
        {
            MediaId = 42,
            MediaType = "Movie",
            StreamIndex = 3,
            SourceLanguage = "en"
        });

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<QueueWithSubtitleResponse>(okResult.Value);
        Assert.True(payload.Success);
        Assert.Equal(0, payload.TranslationsQueued);

        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true),
            Times.Never);
    }

    [Fact]
    public async Task ReconcileSubtitleOutputsForMedia_ReconcilesOnlyRequestedMovie()
    {
        await using var context = BuildContext();
        context.Movies.Add(new Movie
        {
            RadarrId = 7,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = "C:\\media",
            DateAdded = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var reconciliationService = new Mock<ISubtitleOutputReconciliationService>();
        reconciliationService
            .Setup(service => service.ReconcileMediaOutputsAsync(1, MediaType.Movie, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleOutputReconciliationResponse
            {
                MediaItemsScanned = 1,
                BackfilledFiles = 1
            });

        var controller = CreateController(
            context,
            new Mock<ITranslationRequestService>().Object,
            new Mock<ISubtitleExtractionService>().Object,
            new Mock<ISettingService>().Object,
            reconciliationService.Object);

        var response = await controller.ReconcileSubtitleOutputsForMedia(
            new TranslateMediaRequest
            {
                MediaId = 1,
                MediaType = MediaType.Movie
            },
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<SubtitleOutputReconciliationResponse>(okResult.Value);
        Assert.Equal(1, payload.MediaItemsScanned);
        Assert.Equal(1, payload.BackfilledFiles);

        reconciliationService.Verify(
            service => service.ReconcileMediaOutputsAsync(1, MediaType.Movie, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileSubtitleOutputsForMedia_ReturnsNotFoundForMissingMovie()
    {
        await using var context = BuildContext();
        var reconciliationService = new Mock<ISubtitleOutputReconciliationService>();

        var controller = CreateController(
            context,
            new Mock<ITranslationRequestService>().Object,
            new Mock<ISubtitleExtractionService>().Object,
            new Mock<ISettingService>().Object,
            reconciliationService.Object);

        var response = await controller.ReconcileSubtitleOutputsForMedia(
            new TranslateMediaRequest
            {
                MediaId = 999,
                MediaType = MediaType.Movie
            },
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        var payload = Assert.IsType<SubtitleOutputReconciliationResponse>(notFound.Value);
        Assert.Contains("Movie not found", payload.Errors);
        reconciliationService.Verify(
            service => service.ReconcileMediaOutputsAsync(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static TranslateController CreateController(
        LingarrDbContext context,
        ITranslationRequestService translationRequestService,
        ISubtitleExtractionService extractionService,
        ISettingService settingService,
        ISubtitleOutputReconciliationService? subtitleOutputReconciliationService = null)
    {
        return new TranslateController(
            new Mock<ITranslationServiceFactory>().Object,
            translationRequestService,
            new Mock<IMediaSubtitleProcessor>().Object,
            extractionService,
            new Mock<ISubtitleService>().Object,
            subtitleOutputReconciliationService ?? new Mock<ISubtitleOutputReconciliationService>().Object,
            context,
            settingService,
            NullLogger<TranslateController>.Instance);
    }
}
