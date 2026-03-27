using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class StatisticsServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly SqliteConnection _connection;

    public StatisticsServiceTests()
    {
        // Use SQLite in-memory database for raw SQL support
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        
        services.AddDbContext<LingarrDbContext>(opt => opt.UseSqlite(_connection).UseSnakeCaseNamingConvention());
        services.AddMemoryCache();
        
        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _cache = _serviceProvider.GetRequiredService<IMemoryCache>();
        
        // Ensure database is created
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Close();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task GetStatistics_ShouldCacheResult()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);

        // Act - First call
        var result1 = await service.GetStatistics();

        // Act - Second call (should come from cache)
        var result2 = await service.GetStatistics();

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(result1.Id, result2.Id);
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnCachedData_AfterUpdate()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);

        // Get initial stats
        var initialStats = await service.GetStatistics();
        var initialFiles = initialStats.TotalFilesTranslated;

        // Act - Update statistics
        var request = new TranslationRequest
        {
            Title = "Test Movie",
            SourceLanguage = "en",
            TargetLanguage = "nl",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed
        };
        
        await service.UpdateTranslationStatisticsFromLines(
            request,
            "openai",
            new BatchTranslatedLine[] { new() { Line = "test line" } });

        // Get stats again - cache should be invalidated
        var updatedStats = await service.GetStatistics();

        // Assert
        Assert.Equal(initialFiles + 1, updatedStats.TotalFilesTranslated);
    }

    [Fact]
    public async Task UpdateTranslationStatisticsFromLines_ShouldIncrementAtomically()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);
        
        var request = new TranslationRequest
        {
            Title = "Test Movie",
            SourceLanguage = "en",
            TargetLanguage = "de",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed
        };

        var lines = new BatchTranslatedLine[]
        {
            new() { Line = "Hello world" },
            new() { Line = "Test line 2" }
        };

        // Act
        await service.UpdateTranslationStatisticsFromLines(request, "deepl", lines);
        await service.UpdateTranslationStatisticsFromLines(request, "deepl", lines);

        // Get fresh stats
        var stats = await service.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalFilesTranslated);
        Assert.Equal(4, stats.TotalLinesTranslated); // 2 lines * 2 updates
    }

    [Fact]
    public async Task GetDailyStatistics_ShouldCacheResult()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);

        // Act
        var result1 = await service.GetDailyStatistics(7);
        var result2 = await service.GetDailyStatistics(7);

        // Assert
        Assert.NotNull(result1);
        Assert.Same(result1, result2); // Should be same cached reference
    }

    [Fact]
    public async Task InvalidateCache_ShouldRemoveCachedData()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);
        
        // Cache the stats
        await service.GetStatistics();

        // Act - Invalidate cache
        service.InvalidateCache();

        // Assert - Cache should be cleared
        Assert.False(_cache.TryGetValue("dashboard_statistics", out _));
    }

    [Fact]
    public async Task UpdateTranslationStatistics_ShouldUpdateServiceTypeCount()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);
        
        var request = new TranslationRequest
        {
            Title = "Test Movie",
            SourceLanguage = "en",
            TargetLanguage = "fr",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed
        };

        // Act
        await service.UpdateTranslationStatisticsFromLines(
            request,
            "anthropic",
            new BatchTranslatedLine[] { new() { Line = "test" } });

        var stats = await service.GetStatistics();

        // Assert
        Assert.True(stats.TranslationsByService.ContainsKey("anthropic"));
        Assert.Equal(1, stats.TranslationsByService["anthropic"]);
    }

    [Fact]
    public async Task UpdateTranslationStatistics_ShouldUpdateLanguageCount()
    {
        // Arrange
        var service = new StatisticsService(_scopeFactory, _cache);
        
        var request = new TranslationRequest
        {
            Title = "Test Movie",
            SourceLanguage = "en",
            TargetLanguage = "es",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Completed
        };

        // Act
        await service.UpdateTranslationStatisticsFromLines(
            request,
            "google",
            new BatchTranslatedLine[] { new() { Line = "hola" } });

        var stats = await service.GetStatistics();

        // Assert
        Assert.True(stats.SubtitlesByLanguage.ContainsKey("es"));
        Assert.Equal(1, stats.SubtitlesByLanguage["es"]);
    }
}
