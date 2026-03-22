using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TokenUsageServiceTests
{
    [Fact]
    public async Task GetUsageAsync_WithMidnightReset_UsesCurrentUtcDayWindow()
    {
        var nowUtc = Utc(2026, 3, 22, 16, 0);
        await using var dbContext = BuildContext(
        [
            CreateLog("gemini", Utc(2026, 3, 21, 23, 59), completionTokens: 20),
            CreateLog("gemini", Utc(2026, 3, 22, 10, 0), completionTokens: 30),
            CreateLog("gemini", Utc(2026, 3, 22, 12, 0), tokensUsed: 10)
        ]);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(dbContext, cache, nowUtc, "00:00");

        var usage = await service.GetUsageAsync("gemini");

        Assert.Equal(40, usage.TokensUsedToday);
        Assert.Equal(Utc(2026, 3, 23, 0, 0), usage.ResetAt);
    }

    [Fact]
    public async Task GetUsageAsync_BeforeResetHour_UsesPreviousDayWindow()
    {
        var nowUtc = Utc(2026, 3, 22, 5, 0);
        await using var dbContext = BuildContext(
        [
            CreateLog("gemini", Utc(2026, 3, 21, 5, 59), completionTokens: 7),
            CreateLog("gemini", Utc(2026, 3, 21, 6, 0), completionTokens: 11),
            CreateLog("gemini", Utc(2026, 3, 22, 4, 30), completionTokens: 13)
        ]);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(dbContext, cache, nowUtc, "06:00");

        var usage = await service.GetUsageAsync("gemini");

        Assert.Equal(24, usage.TokensUsedToday);
        Assert.Equal(Utc(2026, 3, 22, 6, 0), usage.ResetAt);
    }

    [Fact]
    public async Task GetUsageAsync_AfterResetHour_UsesCurrentDayWindow()
    {
        var nowUtc = Utc(2026, 3, 22, 7, 0);
        await using var dbContext = BuildContext(
        [
            CreateLog("gemini", Utc(2026, 3, 22, 5, 59), completionTokens: 9),
            CreateLog("gemini", Utc(2026, 3, 22, 6, 0), completionTokens: 15),
            CreateLog("gemini", Utc(2026, 3, 22, 6, 30), completionTokens: 21)
        ]);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(dbContext, cache, nowUtc, "06:00");

        var usage = await service.GetUsageAsync("gemini");

        Assert.Equal(36, usage.TokensUsedToday);
        Assert.Equal(Utc(2026, 3, 23, 6, 0), usage.ResetAt);
    }

    [Fact]
    public async Task GetUsageAsync_WhenResetTimeChanges_RecalculatesUsingNewWindow()
    {
        var nowUtc = Utc(2026, 3, 22, 5, 0);
        await using var dbContext = BuildContext(
        [
            CreateLog("gemini", Utc(2026, 3, 21, 12, 0), completionTokens: 10),
            CreateLog("gemini", Utc(2026, 3, 22, 1, 0), completionTokens: 20)
        ]);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resetTime = "00:00";
        var service = CreateService(dbContext, cache, nowUtc, () => resetTime);

        var initialUsage = await service.GetUsageAsync("gemini");
        resetTime = "06:00";
        var updatedUsage = await service.GetUsageAsync("gemini");

        Assert.Equal(20, initialUsage.TokensUsedToday);
        Assert.Equal(30, updatedUsage.TokensUsedToday);
        Assert.Equal(Utc(2026, 3, 22, 6, 0), updatedUsage.ResetAt);
    }

    [Fact]
    public async Task RecordUsageAsync_UpdatesCachedSnapshotForCurrentWindow()
    {
        var nowUtc = Utc(2026, 3, 22, 16, 0);
        await using var dbContext = BuildContext(
        [
            CreateLog("gemini", Utc(2026, 3, 22, 10, 0), completionTokens: 10)
        ]);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(dbContext, cache, nowUtc, "00:00");

        var usageBefore = await service.GetUsageAsync("gemini");
        await service.RecordUsageAsync("gemini", promptTokens: 20, completionTokens: 5);
        var usageAfter = await service.GetUsageAsync("gemini");

        Assert.Equal(10, usageBefore.TokensUsedToday);
        Assert.Equal(15, usageAfter.TokensUsedToday);
    }

    private static TokenUsageService CreateService(
        LingarrDbContext dbContext,
        IMemoryCache cache,
        DateTime nowUtc,
        string resetTime)
    {
        return CreateService(dbContext, cache, nowUtc, () => resetTime);
    }

    private static TokenUsageService CreateService(
        LingarrDbContext dbContext,
        IMemoryCache cache,
        DateTime nowUtc,
        Func<string> resetTimeProvider)
    {
        var settingsMock = new Mock<ISettingService>();
        settingsMock
            .Setup(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => key switch
            {
                SettingKeys.Translation.TokenLimits.TokenLimitResetTime => resetTimeProvider(),
                SettingKeys.Translation.TokenLimits.GeminiTokenLimit => "1000",
                _ => string.Empty
            });

        return new TokenUsageService(
            dbContext,
            settingsMock.Object,
            NullLogger<TokenUsageService>.Instance,
            cache,
            new StubTimeProvider(nowUtc));
    }

    private static async Task<LingarrDbContext> BuildContext(IEnumerable<ApiUsageLog> logs)
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new LingarrDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.ApiUsageLogs.AddRangeAsync(logs);
        await context.SaveChangesAsync();
        return context;
    }

    private static ApiUsageLog CreateLog(string service, DateTime timestampUtc, int? completionTokens = null, int? tokensUsed = null)
    {
        return new ApiUsageLog
        {
            Service = service,
            Timestamp = timestampUtc,
            CompletionTokens = completionTokens,
            TokensUsed = tokensUsed,
            Success = true,
            ResponseTimeMs = 150
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private sealed class StubTimeProvider(DateTime nowUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = new(nowUtc, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
