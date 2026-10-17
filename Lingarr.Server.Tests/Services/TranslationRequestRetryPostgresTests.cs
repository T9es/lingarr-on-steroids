using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public sealed class TranslationRequestRetryPostgresTests : IClassFixture<TranslationRequestPostgresFixture>
{
    private readonly TranslationRequestPostgresFixture _fixture;

    public TranslationRequestRetryPostgresTests(TranslationRequestPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RetryAllFailedRequests_BlocksActiveDedupeRaceAndContinues()
    {
        await using var database = await _fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            401,
            "library:Movie:401",
            "Failed race request",
            TranslationStatus.Failed,
            now.AddMinutes(-2));
        var unrelatedRequest = CreateRequest(
            2,
            402,
            "library:Movie:402",
            "Unrelated failed request",
            TranslationStatus.Failed,
            now.AddMinutes(-1));
        context.TranslationRequests.AddRange(failedRequest, unrelatedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        service.BeforeFailedRequestRetryAsync = async requestId =>
        {
            if (requestId != failedRequest.Id)
            {
                return;
            }

            await using var racingContext = database.CreateContext();
            racingContext.TranslationRequests.Add(CreateRequest(
                3,
                401,
                "library:Movie:401",
                "Active owner",
                TranslationStatus.Pending,
                now,
                true));
            await racingContext.SaveChangesAsync();
        };

        var result = await service.RetryAllFailedRequests();

        Assert.Equal(2, result.TotalFailed);
        Assert.Equal(1, result.Retried);
        Assert.Equal(1, result.BlockedByActiveRequest);
        Assert.Equal(1, result.RemainingFailed);

        context.ChangeTracker.Clear();
        Assert.Equal(
            TranslationStatus.Failed,
            (await context.TranslationRequests.SingleAsync(request => request.Id == failedRequest.Id)).Status);
        Assert.Equal(
            TranslationStatus.Pending,
            (await context.TranslationRequests.SingleAsync(request => request.Id == unrelatedRequest.Id)).Status);
    }

    [Fact]
    public async Task RetryTranslationRequest_ReturnsBlockedWhenActiveDedupeRaceOccurs()
    {
        await using var database = await _fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            411,
            "library:Movie:411",
            "Failed race request",
            TranslationStatus.Failed,
            now.AddMinutes(-1));
        context.TranslationRequests.Add(failedRequest);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        service.BeforeFailedRequestRetryAsync = async requestId =>
        {
            Assert.Equal(failedRequest.Id, requestId);
            await using var racingContext = database.CreateContext();
            racingContext.TranslationRequests.Add(CreateRequest(
                2,
                411,
                "library:Movie:411",
                "Active owner",
                TranslationStatus.Pending,
                now,
                true));
            await racingContext.SaveChangesAsync();
        };

        var result = await service.RetryTranslationRequest(failedRequest);

        Assert.NotNull(result);
        Assert.False(result!.Retried);
        Assert.True(result.BlockedByActiveRequest);
        context.ChangeTracker.Clear();
        Assert.Equal(
            TranslationStatus.Failed,
            (await context.TranslationRequests.SingleAsync(request => request.Id == failedRequest.Id)).Status);
    }

    [Fact]
    public async Task RetryTranslationRequest_PropagatesUnrelatedUniqueIndexViolation()
    {
        await using var database = await _fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();

        var now = DateTime.UtcNow;
        var failedRequest = CreateRequest(
            1,
            421,
            "library:Movie:421",
            "Retry candidate",
            TranslationStatus.Failed,
            now.AddMinutes(-1));
        var existingRequest = CreateRequest(
            2,
            422,
            "library:Movie:422",
            "Occupied title",
            TranslationStatus.Failed,
            now);
        context.TranslationRequests.AddRange(failedRequest, existingRequest);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX ux_test_translation_request_title ON translation_requests (title)");
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION force_retry_title_collision() RETURNS trigger AS $$
            BEGIN
                IF NEW.is_active IS TRUE AND OLD.is_active IS DISTINCT FROM TRUE THEN
                    NEW.title := 'Occupied title';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER trg_test_retry_title_collision
            BEFORE UPDATE ON translation_requests
            FOR EACH ROW EXECUTE FUNCTION force_retry_title_collision()
            """);

        var service = CreateService(context);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            service.RetryTranslationRequest(failedRequest));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_test_translation_request_title", exception.ConstraintName);
    }

    private static TranslationRequest CreateRequest(
        int id,
        int mediaId,
        string workloadItemKey,
        string title,
        TranslationStatus status,
        DateTime createdAt,
        bool isActive = false)
    {
        return new TranslationRequest
        {
            Id = id,
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = workloadItemKey,
            MediaId = mediaId,
            MediaType = MediaType.Movie,
            Title = title,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = $"/movies/{mediaId}.en.srt",
            SourceSubtitleFormat = ".srt",
            RequiredOutputFormats = ".srt",
            Status = status,
            IsActive = isActive ? (bool?)true : null,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static TranslationRequestService CreateService(LingarrDbContext context)
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(client => client.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(clients => clients.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<TranslationRequestsHub>>();
        hubContextMock.SetupGet(hub => hub.Clients).Returns(hubClientsMock.Object);

        return new TranslationRequestService(
            context,
            new Mock<ITranslationWorkerService>().Object,
            hubContextMock.Object,
            new Mock<ITranslationServiceFactory>().Object,
            new Mock<IProgressService>().Object,
            new Mock<IStatisticsService>().Object,
            new Lazy<IMediaService>(() => new Mock<IMediaService>().Object),
            new Mock<ISettingService>().Object,
            new Mock<IBatchFallbackService>().Object,
            NullLogger<TranslationRequestService>.Instance,
            new Mock<ITranslationCancellationService>().Object,
            new Mock<IMediaStateService>().Object,
            new Mock<ICustomMediaStateService>().Object);
    }
}

public sealed class TranslationRequestPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync()
    {
        return _container.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    public async Task<TranslationRequestPostgresDatabase> CreateDatabaseAsync()
    {
        var databaseName = $"retry_test_{Guid.NewGuid():N}";
        await using (var adminConnection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await adminConnection.OpenAsync();
            await using var command = adminConnection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
        var database = new TranslationRequestPostgresDatabase(
            _container.GetConnectionString(),
            connectionString,
            databaseName);

        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }
}

public sealed class TranslationRequestPostgresDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    public string ConnectionString { get; }

    public TranslationRequestPostgresDatabase(
        string adminConnectionString,
        string connectionString,
        string databaseName)
    {
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        _databaseName = databaseName;
    }

    public LingarrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new LingarrDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();
        await using var command = adminConnection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
