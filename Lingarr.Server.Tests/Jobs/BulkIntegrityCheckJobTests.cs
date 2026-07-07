using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class BulkIntegrityCheckJobTests
{
    [Fact]
    public async Task Execute_WhenAutoQueueDisabled_ReportsCorruptMediaWithoutQueueing()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Movies.Add(new Movie
        {
            Id = 1,
            RadarrId = 10,
            Title = "Movie",
            Path = "/movies",
            FileName = "movie.mkv",
            DateAdded = DateTime.UtcNow,
            TranslationState = TranslationState.Complete
        });
        await dbContext.SaveChangesAsync();

        var processorMock = new Mock<IMediaSubtitleProcessor>();
        processorMock
            .Setup(processor => processor.ProcessMediaForceAsync(
                It.IsAny<Movie>(),
                MediaType.Movie,
                true,
                false,
                false,
                false,
                25,
                It.IsAny<ICollection<SubtitleIntegrityFinding>>()))
            .Callback<IMedia, MediaType, bool, bool, bool, bool, int?, ICollection<SubtitleIntegrityFinding>>(
                (_, _, _, _, _, _, _, findings) => findings.Add(new SubtitleIntegrityFinding
                {
                    MediaId = 1,
                    MediaType = MediaType.Movie.ToString(),
                    MediaTitle = "Movie",
                    SourceLanguage = "eng",
                    TargetLanguage = "pol",
                    Reason = "Target has too few entries.",
                    SourcePath = "/movies/movie.eng.srt",
                    TargetPath = "/movies/movie.pol.srt",
                    SourceEntries = 100,
                    TargetEntries = 1,
                    MinimumTargetEntries = 95
                }))
            .ReturnsAsync(1);

        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.BulkIntegrityAutoQueue))
            .ReturnsAsync("false");
        settingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleValidation.BulkIntegrityMaxAutoQueuePerRun))
            .ReturnsAsync("25");

        var job = new BulkIntegrityCheckJob(
            dbContext,
            processorMock.Object,
            Mock.Of<ISubtitleIntegrityService>(),
            CreateHubContext(),
            settingServiceMock.Object,
            NullLogger<BulkIntegrityCheckJob>.Instance);

        await job.Execute();

        Assert.NotNull(BulkIntegrityStats.Current);
        Assert.Equal(1, BulkIntegrityStats.Current!.CorruptCount);
        Assert.Equal(0, BulkIntegrityStats.Current.QueuedCount);
        var finding = Assert.Single(BulkIntegrityStats.Current.FlaggedItems);
        Assert.Equal("/movies/movie.pol.srt", finding.TargetPath);
        Assert.Equal(100, finding.SourceEntries);
        Assert.False(finding.IsQueued);
        processorMock.Verify(processor => processor.ProcessMediaForceAsync(
            It.IsAny<Movie>(),
            MediaType.Movie,
            true,
            false,
            false,
            false,
            25,
            It.IsAny<ICollection<SubtitleIntegrityFinding>>()), Times.Once);
    }

    private static LingarrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LingarrDbContext(options);
    }

    private static IHubContext<JobProgressHub> CreateHubContext()
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                default))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        clientsMock
            .Setup(clients => clients.Group("JobProgress"))
            .Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<JobProgressHub>>();
        hubContextMock
            .Setup(context => context.Clients)
            .Returns(clientsMock.Object);
        return hubContextMock.Object;
    }
}
