using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public sealed class TranslationWorkerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;

    public TranslationWorkerServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task TryClaimPendingRequestAsync_AssignsUniqueOwnerToken()
    {
        var request = CreateRequest(TranslationStatus.Pending);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var ownershipToken = await TranslationWorkerService.TryClaimPendingRequestAsync(
            _dbContext,
            request.Id,
            CancellationToken.None);

        var persistedRequest = await _dbContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);

        Assert.NotNull(ownershipToken);
        Assert.NotEqual(string.Empty, ownershipToken);
        Assert.Equal(ownershipToken, persistedRequest.JobId);
        Assert.Equal(TranslationStatus.InProgress, persistedRequest.Status);
        Assert.True(persistedRequest.IsActive);
    }

    [Fact]
    public async Task TryClaimPendingRequestAsync_CannotOverwriteExistingAttemptOwner()
    {
        var request = CreateRequest(TranslationStatus.Pending);
        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var firstOwner = await TranslationWorkerService.TryClaimPendingRequestAsync(
            _dbContext,
            request.Id,
            CancellationToken.None);
        var secondOwner = await TranslationWorkerService.TryClaimPendingRequestAsync(
            _dbContext,
            request.Id,
            CancellationToken.None);

        var persistedRequest = await _dbContext.TranslationRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);

        Assert.NotNull(firstOwner);
        Assert.Null(secondOwner);
        Assert.Equal(firstOwner, persistedRequest.JobId);
        Assert.Equal(TranslationStatus.InProgress, persistedRequest.Status);
    }

    [Fact]
    public void WorkerTaskOwnership_PreventsOldTaskFromRemovingReplacementTask()
    {
        var service = new TranslationWorkerService(
            Mock.Of<IServiceProvider>(),
            NullLogger<TranslationWorkerService>.Instance);
        var oldTask = new TaskCompletionSource<object?>().Task;
        var replacementTask = new TaskCompletionSource<object?>().Task;

        Assert.True(service.TryRegisterWorkerTask(
            8,
            "old-attempt",
            oldTask,
            TranslationWorkloadKind.Library));
        Assert.True(service.TryRegisterWorkerTask(
            8,
            "replacement-attempt",
            replacementTask,
            TranslationWorkloadKind.Library));
        Assert.Equal(1, service.ActiveWorkers);

        Assert.False(service.TryRemoveWorkerTask(8, "old-attempt", oldTask));
        Assert.Equal(1, service.ActiveWorkers);
        Assert.True(service.TryRemoveWorkerTask(8, "replacement-attempt", replacementTask));
        Assert.Equal(0, service.ActiveWorkers);
    }

    private static TranslationRequest CreateRequest(TranslationStatus status)
    {
        return new TranslationRequest
        {
            Title = "Ownership test",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = status,
            IsActive = status == TranslationStatus.Pending
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
