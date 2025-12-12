using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class TranslationJobRescheduleTests
{
    [Fact]
    public async Task ExecuteNormal_WhenNoSlotsAvailable_ReschedulesAndUpdatesJobId()
    {
        await using var context = BuildContext();

        var request = new TranslationRequest
        {
            Id = 1,
            MediaId = 10,
            MediaType = MediaType.Movie,
            Title = "Test Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            Status = TranslationStatus.Pending,
            SubtitleToTranslate = null
        };

        context.TranslationRequests.Add(request);
        await context.SaveChangesAsync();

        var limiterMock = new Mock<IParallelTranslationLimiter>();
        limiterMock.SetupGet(l => l.AvailableSlots).Returns(0);
        limiterMock.SetupGet(l => l.MaxConcurrency).Returns(1);

        var backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        backgroundJobClientMock
            .Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("new-job-id");

        var cancellationServiceMock = new Mock<ITranslationCancellationService>();
        cancellationServiceMock.Setup(c => c.RegisterJob(It.IsAny<int>())).Returns(CancellationToken.None);

        var job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            new Mock<ISettingService>().Object,
            context,
            new Mock<IProgressService>().Object,
            new Mock<ISubtitleService>().Object,
            new Mock<IScheduleService>().Object,
            new Mock<IStatisticsService>().Object,
            new Mock<ITranslationServiceFactory>().Object,
            new Mock<ITranslationRequestService>().Object,
            limiterMock.Object,
            new Mock<IBatchFallbackService>().Object,
            new Mock<ISubtitleExtractionService>().Object,
            cancellationServiceMock.Object,
            backgroundJobClientMock.Object);

        await job.ExecuteNormal(request, CancellationToken.None);

        backgroundJobClientMock.Verify(
            c => c.Create(It.IsAny<Job>(), It.IsAny<ScheduledState>()),
            Times.Once);
        limiterMock.Verify(
            l => l.AcquireAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var updated = await context.TranslationRequests.FindAsync(request.Id);
        Assert.Equal("new-job-id", updated?.JobId);
        Assert.Equal(TranslationStatus.Pending, updated?.Status);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }
}

