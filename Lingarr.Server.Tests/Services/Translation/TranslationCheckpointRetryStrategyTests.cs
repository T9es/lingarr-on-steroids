using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationCheckpointRetryStrategyTests : IAsyncLifetime
{
    private const int SaveRequestId = 424201;
    private const int DeleteRequestId = 424202;
    private const int SaveCheckpointRequestId = 424203;

    private PostgreSqlContainer _container = null!;
    private string _checkpointRoot = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();
        await _container.StartAsync();

        _checkpointRoot = Path.Combine(
            Path.GetTempPath(),
            "lingarr-checkpoint-tests",
            Guid.NewGuid().ToString("N"));
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_checkpointRoot))
        {
            Directory.Delete(_checkpointRoot, recursive: true);
        }

        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task SaveTranslationAsync_WithOwnershipToken_DoesNotThrowWithRetryingExecutionStrategy()
    {
        const string ownershipToken = "attempt-token-save";

        await using var db = CreateContext();
        await SeedRequest(db, SaveRequestId, ownershipToken);

        var service = CreateService(db);
        var exception = await Record.ExceptionAsync(() => service.SaveTranslationAsync(
            SaveRequestId,
            "fingerprint-1",
            position: 1,
            "Translated text",
            CancellationToken.None,
            ownershipToken));

        Assert.Null(exception);
        Assert.True(File.Exists(Path.Combine(_checkpointRoot, $"{SaveRequestId}.json")));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task DeleteAsync_WithOwnershipToken_DoesNotThrowWithRetryingExecutionStrategy()
    {
        const string ownershipToken = "attempt-token-delete";

        await using var db = CreateContext();
        await SeedRequest(db, DeleteRequestId, ownershipToken);

        var checkpointPath = Path.Combine(_checkpointRoot, $"{DeleteRequestId}.json");
        Directory.CreateDirectory(_checkpointRoot);
        await File.WriteAllTextAsync(checkpointPath, "{}");

        var service = CreateService(db);
        var exception = await Record.ExceptionAsync(() =>
            service.DeleteAsync(DeleteRequestId, CancellationToken.None, ownershipToken));

        Assert.Null(exception);
        Assert.False(File.Exists(checkpointPath));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task SaveCheckpointAsync_WithOwnershipToken_DoesNotThrowWithRetryingExecutionStrategy()
    {
        const string ownershipToken = "attempt-token-save-checkpoint";

        await using var db = CreateContext();
        await SeedRequest(db, SaveCheckpointRequestId, ownershipToken);

        var service = CreateService(db);
        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = SaveCheckpointRequestId,
            SourceFingerprint = "fingerprint-checkpoint",
            Translations = new Dictionary<int, string> { [1] = "Translated checkpoint text" }
        };

        var exception = await Record.ExceptionAsync(() =>
            service.SaveCheckpointAsync(checkpoint, CancellationToken.None, ownershipToken));

        Assert.Null(exception);
        Assert.True(File.Exists(Path.Combine(_checkpointRoot, $"{SaveCheckpointRequestId}.json")));
        Assert.Null(db.Database.CurrentTransaction);
    }

    private LingarrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new LingarrDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private TranslationCheckpointService CreateService(LingarrDbContext db)
    {
        return new TranslationCheckpointService(
            NullLogger<TranslationCheckpointService>.Instance,
            _checkpointRoot,
            beforeCheckpointWriteAsync: null,
            dbContext: db);
    }

    private static async Task SeedRequest(LingarrDbContext db, int id, string ownershipToken)
    {
        db.TranslationRequests.Add(new TranslationRequest
        {
            Id = id,
            Title = $"Retry strategy request {id}",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.InProgress,
            JobId = ownershipToken,
            SubtitleToTranslate = "/tmp/source.srt"
        });
        await db.SaveChangesAsync();
    }
}
