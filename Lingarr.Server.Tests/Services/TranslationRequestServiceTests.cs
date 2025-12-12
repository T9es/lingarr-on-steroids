using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationRequestServiceTests
{
    [Fact]
    public async Task DedupeQueuedRequests_RemovesDuplicatePendingRequests()
    {
        await using var context = BuildContext();

        var now = DateTime.UtcNow;
        var requests = new List<TranslationRequest>
        {
            CreateRequest(1, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now),
            CreateRequest(2, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now.AddSeconds(1)),
            CreateRequest(3, 10, MediaType.Movie, "en", "ro", "/movies/a.en.srt", TranslationStatus.Pending, now.AddSeconds(2)),
            CreateRequest(4, 11, MediaType.Movie, "en", "ro", "/movies/b.en.srt", TranslationStatus.Pending, now),
            CreateRequest(5, 11, MediaType.Movie, "en", "ro", "/movies/b.en.srt", TranslationStatus.Pending, now.AddSeconds(1))
        };

        context.TranslationRequests.AddRange(requests);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var (removed, skipped) = await service.DedupeQueuedRequests();

        Assert.Equal(3, removed);
        Assert.Equal(0, skipped);

        var remaining = await context.TranslationRequests
            .Where(tr => tr.Status == TranslationStatus.Pending)
            .ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, tr => tr.MediaId == 10 && tr.SubtitleToTranslate == "/movies/a.en.srt");
        Assert.Contains(remaining, tr => tr.MediaId == 11 && tr.SubtitleToTranslate == "/movies/b.en.srt");
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static TranslationRequest CreateRequest(
        int id,
        int mediaId,
        MediaType mediaType,
        string sourceLanguage,
        string targetLanguage,
        string subtitlePath,
        TranslationStatus status,
        DateTime createdAt)
    {
        return new TranslationRequest
        {
            Id = id,
            MediaId = mediaId,
            MediaType = mediaType,
            Title = $"Media {mediaId}",
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            SubtitleToTranslate = subtitlePath,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static TranslationRequestService CreateService(LingarrDbContext context)
    {
        var backgroundJobClient = new Mock<IBackgroundJobClient>();

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<TranslationRequestsHub>>();
        hubContextMock.SetupGet(h => h.Clients).Returns(hubClientsMock.Object);

        return new TranslationRequestService(
            context,
            backgroundJobClient.Object,
            hubContextMock.Object,
            new Mock<ITranslationServiceFactory>().Object,
            new Mock<IProgressService>().Object,
            new Mock<IStatisticsService>().Object,
            new Mock<IMediaService>().Object,
            new Mock<ISettingService>().Object,
            new Mock<IBatchFallbackService>().Object,
            NullLogger<TranslationRequestService>.Instance,
            new Mock<ITranslationCancellationService>().Object);
    }
}

