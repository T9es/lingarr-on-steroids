using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class PausedTranslationResumeServiceTests
{
    [Fact]
    public async Task ResumeDuePausedRequestsAsync_RequeuesOnlyEligiblePausedRequests()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new LingarrDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        dbContext.TranslationRequests.Add(CreateRequest(TranslationStatus.Paused, now.AddMinutes(-1)));
        dbContext.TranslationRequests.Add(CreateRequest(TranslationStatus.Paused, now.AddHours(1)));
        dbContext.TranslationRequests.Add(CreateRequest(TranslationStatus.Failed, null));
        await dbContext.SaveChangesAsync();

        var workerService = new Mock<ITranslationWorkerService>();
        var service = new PausedTranslationResumeService(
            dbContext,
            workerService.Object,
            NullLogger<PausedTranslationResumeService>.Instance);

        var resumed = await service.ResumeDuePausedRequestsAsync(CancellationToken.None);

        Assert.Equal(1, resumed);
        dbContext.ChangeTracker.Clear();
        var requests = await dbContext.TranslationRequests
            .OrderBy(request => request.Id)
            .ToListAsync();
        Assert.Equal(TranslationStatus.Pending, requests[0].Status);
        Assert.Null(requests[0].PausedAt);
        Assert.Null(requests[0].PauseReason);
        Assert.Null(requests[0].PausedProvider);
        Assert.Null(requests[0].NextRetryAt);
        Assert.Equal(TranslationStatus.Paused, requests[1].Status);
        Assert.Equal(TranslationStatus.Failed, requests[2].Status);
        workerService.Verify(service => service.Signal(), Times.Once);
    }

    private static TranslationRequest CreateRequest(TranslationStatus status, DateTime? nextRetryAt)
    {
        return new TranslationRequest
        {
            Title = $"Request {Guid.NewGuid():N}",
            WorkloadItemKey = $"test:{Guid.NewGuid():N}",
            SourceLanguage = "en",
            TargetLanguage = "nl",
            MediaType = MediaType.Movie,
            Status = status,
            IsActive = status == TranslationStatus.Paused ? true : null,
            PausedAt = status == TranslationStatus.Paused ? DateTime.UtcNow : null,
            PauseReason = status == TranslationStatus.Paused ? "reserve" : null,
            PausedProvider = status == TranslationStatus.Paused ? "nanogpt" : null,
            NextRetryAt = nextRetryAt
        };
    }
}
