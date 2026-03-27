using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Sync;
using Lingarr.Server.Models.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class WebhookJobTests
{
    [Fact]
    public async Task ProcessSonarrWebhook_WhenFileChanged_InterruptsBeforeAutomation()
    {
        var sequence = new MockSequence();
        var automationServiceMock = new Mock<IAutomationService>();
        var mediaServiceMock = new Mock<IMediaService>();
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var translationRequestServiceMock = new Mock<ITranslationRequestService>();

        instanceConfigServiceMock
            .Setup(s => s.GetSonarrConfig("default"))
            .ReturnsAsync(new InstanceConfig("http://sonarr", "api-key", "default"));

        mediaServiceMock
            .InSequence(sequence)
            .Setup(s => s.RefreshEpisodeFromSonarrEpisodeId(10, "default"))
            .ReturnsAsync(new EpisodeRefreshResult(100, true, "old-file", "new-file", DateTime.UtcNow));

        translationRequestServiceMock
            .InSequence(sequence)
            .Setup(s => s.InterruptActiveRequestsForMedia(MediaType.Episode, 100))
            .ReturnsAsync(1);

        automationServiceMock
            .InSequence(sequence)
            .Setup(s => s.ProcessSingleMediaForAutomationAsync(100, MediaType.Episode, "sonarr_webhook"))
            .ReturnsAsync(1);

        var job = new WebhookJob(
            automationServiceMock.Object,
            mediaServiceMock.Object,
            instanceConfigServiceMock.Object,
            translationRequestServiceMock.Object,
            NullLogger<WebhookJob>.Instance);

        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 10, EpisodeNumber = 11, SeasonNumber = 3, Title = "The Beginning" }]
        };

        await job.ProcessSonarrWebhook(payload, "default");

        translationRequestServiceMock.Verify(s => s.InterruptActiveRequestsForMedia(MediaType.Episode, 100), Times.Once);
        automationServiceMock.Verify(s => s.ProcessSingleMediaForAutomationAsync(100, MediaType.Episode, "sonarr_webhook"), Times.Once);
    }

    [Fact]
    public async Task ProcessSonarrWebhook_WhenRefreshFails_SkipsAutomation()
    {
        var automationServiceMock = new Mock<IAutomationService>();
        var mediaServiceMock = new Mock<IMediaService>();
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var translationRequestServiceMock = new Mock<ITranslationRequestService>();

        instanceConfigServiceMock
            .Setup(s => s.GetSonarrConfig("default"))
            .ReturnsAsync(new InstanceConfig("http://sonarr", "api-key", "default"));

        mediaServiceMock
            .Setup(s => s.RefreshEpisodeFromSonarrEpisodeId(10, "default"))
            .ReturnsAsync((EpisodeRefreshResult?)null);

        var job = new WebhookJob(
            automationServiceMock.Object,
            mediaServiceMock.Object,
            instanceConfigServiceMock.Object,
            translationRequestServiceMock.Object,
            NullLogger<WebhookJob>.Instance);

        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 10, EpisodeNumber = 11, SeasonNumber = 3, Title = "The Beginning" }]
        };

        await job.ProcessSonarrWebhook(payload, "default");

        translationRequestServiceMock.Verify(s => s.InterruptActiveRequestsForMedia(It.IsAny<MediaType>(), It.IsAny<int>()), Times.Never);
        automationServiceMock.Verify(s => s.ProcessSingleMediaForAutomationAsync(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRadarrWebhook_WhenInstanceConfigMissing_DoesNotFallbackOrProcess()
    {
        var automationServiceMock = new Mock<IAutomationService>();
        var mediaServiceMock = new Mock<IMediaService>();
        var instanceConfigServiceMock = new Mock<IInstanceConfigService>();
        var translationRequestServiceMock = new Mock<ITranslationRequestService>();

        instanceConfigServiceMock
            .Setup(s => s.GetRadarrConfig("missing"))
            .ReturnsAsync((InstanceConfig?)null);

        var job = new WebhookJob(
            automationServiceMock.Object,
            mediaServiceMock.Object,
            instanceConfigServiceMock.Object,
            translationRequestServiceMock.Object,
            NullLogger<WebhookJob>.Instance);

        var payload = new RadarrWebhookPayload
        {
            EventType = "Download",
            Movie = new RadarrWebhookMovie { Id = 123, Title = "Movie" }
        };

        await job.ProcessRadarrWebhook(payload, "missing");

        mediaServiceMock.Verify(s => s.GetMovieIdOrSyncFromRadarrMovieId(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
        automationServiceMock.Verify(s => s.ProcessSingleMediaForAutomationAsync(It.IsAny<int>(), It.IsAny<MediaType>(), It.IsAny<string>()), Times.Never);
    }
}
