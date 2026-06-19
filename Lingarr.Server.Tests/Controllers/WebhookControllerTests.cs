using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Server.Controllers;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class WebhookControllerTests
{
    [Fact]
    public void SonarrWebhook_IgnoresUnsupportedEventType()
    {
        var backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        var controller = new WebhookController(backgroundJobClientMock.Object, NullLogger<WebhookController>.Instance);

        var payload = new SonarrWebhookPayload
        {
            EventType = "Grab",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 10, EpisodeNumber = 1, SeasonNumber = 1, Title = "Ep" }]
        };

        var result = controller.SonarrWebhook(payload, "default");

        Assert.IsType<OkObjectResult>(result);
        backgroundJobClientMock.Verify(
            c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public void SonarrWebhook_IgnoresUnsupportedEventTypeWithoutSeriesData()
    {
        var backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        var controller = new WebhookController(backgroundJobClientMock.Object, NullLogger<WebhookController>.Instance);

        var payload = new SonarrWebhookPayload
        {
            EventType = "HealthRestored"
        };

        var result = controller.SonarrWebhook(payload, "default");

        Assert.IsType<OkObjectResult>(result);
        backgroundJobClientMock.Verify(
            c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public void SonarrWebhook_QueuesDownloadEvent()
    {
        var backgroundJobClientMock = new Mock<IBackgroundJobClient>();
        backgroundJobClientMock
            .Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("job-1");

        var controller = new WebhookController(backgroundJobClientMock.Object, NullLogger<WebhookController>.Instance);

        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 10, EpisodeNumber = 1, SeasonNumber = 1, Title = "Ep" }]
        };

        var result = controller.SonarrWebhook(payload, "default");

        Assert.IsType<OkObjectResult>(result);
        backgroundJobClientMock.Verify(
            c => c.Create(
                It.Is<Job>(job => job.Type == typeof(WebhookJob) && job.Method.Name == nameof(WebhookJob.ProcessSonarrWebhook)),
                It.IsAny<EnqueuedState>()),
            Times.Once);
    }
}
