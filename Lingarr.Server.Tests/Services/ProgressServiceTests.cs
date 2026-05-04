using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class ProgressServiceTests
{
    [Fact]
    public async Task Emit_ThrottlesRapidIntermediateUpdatesForSameRequest()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        await using var provider = BuildProvider(connection);
        await SeedRequest(provider, TranslationStatus.InProgress);

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progressService = CreateProgressService(provider, clientProxyMock);
        var request = CreateRequest(TranslationStatus.InProgress);

        await progressService.Emit(request, 10);
        await progressService.Emit(request, 20);
        await progressService.Emit(request, 30);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Emit_AlwaysSendsTerminalProgressInsideThrottleWindow()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        await using var provider = BuildProvider(connection);
        await SeedRequest(provider, TranslationStatus.Completed);

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progressService = CreateProgressService(provider, clientProxyMock);

        await progressService.Emit(CreateRequest(TranslationStatus.InProgress), 10);
        await progressService.Emit(CreateRequest(TranslationStatus.Completed), 100);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Emit_ClearsThrottleStateAfterTerminalProgress()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        await using var provider = BuildProvider(connection);
        await SeedRequest(provider, TranslationStatus.InProgress);

        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var progressService = CreateProgressService(provider, clientProxyMock);

        await progressService.Emit(CreateRequest(TranslationStatus.InProgress), 10);
        await progressService.Emit(CreateRequest(TranslationStatus.Completed), 100);
        await progressService.Emit(CreateRequest(TranslationStatus.InProgress), 20);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "RequestProgress",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(options =>
            options.UseSqlite(connection).UseSnakeCaseNamingConvention());

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<LingarrDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static async Task SeedRequest(ServiceProvider provider, TranslationStatus status)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        context.TranslationRequests.Add(CreateRequest(status));
        await context.SaveChangesAsync();
    }

    private static ProgressService CreateProgressService(
        ServiceProvider provider,
        Mock<IClientProxy> clientProxyMock)
    {
        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock
            .Setup(clients => clients.Group("TranslationRequests"))
            .Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<TranslationRequestsHub>>();
        hubContextMock
            .SetupGet(context => context.Clients)
            .Returns(hubClientsMock.Object);

        return new ProgressService(
            hubContextMock.Object,
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static TranslationRequest CreateRequest(TranslationStatus status)
    {
        var now = DateTime.UtcNow;
        return new TranslationRequest
        {
            Id = 1,
            MediaId = 100,
            MediaType = MediaType.Movie,
            Title = "Movie 100",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = "/movies/movie.en.srt",
            Status = status,
            StartedAt = now,
            CompletedAt = status == TranslationStatus.Completed ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
