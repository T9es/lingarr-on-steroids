using System;
using System.Collections.Generic;
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
    public async Task QueueWithSubtitle_EnqueuesWhenActiveRequestUsesDifferentRequiredOutputFormats()
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
        Assert.Equal(1, payload.TranslationsQueued);

        translationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), true),
            Times.Once);
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
        ISettingService settingService)
    {
        return new TranslateController(
            new Mock<ITranslationServiceFactory>().Object,
            translationRequestService,
            new Mock<IMediaSubtitleProcessor>().Object,
            extractionService,
            new Mock<ISubtitleService>().Object,
            context,
            settingService,
            NullLogger<TranslateController>.Instance);
    }
}
