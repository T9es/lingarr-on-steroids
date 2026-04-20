using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class StartupServiceTests
{
    [Fact]
    public async Task StartAsync_SeedsSkipWhenTargetEmbeddedDefault()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<LingarrDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();

        var service = new StartupService(provider, NullLogger<StartupService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var skipWhenTargetEmbedded = await context.Settings
            .SingleAsync(setting => setting.Key == SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded);

        Assert.Equal("true", skipWhenTargetEmbedded.Value);
    }
}
