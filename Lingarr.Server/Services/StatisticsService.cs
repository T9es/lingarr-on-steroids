using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lingarr.Server.Services;

public class StatisticsService : IStatisticsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);
    private const string StatisticsCacheKey = "dashboard_statistics";
    private const string DailyStatisticsCacheKey = "daily_statistics_{0}";
    private const string HourlyStatisticsCacheKey = "hourly_statistics_{0:yyyy-MM-dd}";

    public StatisticsService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
    }

    public async Task<Statistics> GetStatistics()
    {
        // Check cache first
        if (_cache.TryGetValue(StatisticsCacheKey, out Statistics? cachedStats) && cachedStats != null)
        {
            return cachedStats;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var stats = await GetStatisticsForRead(dbContext);
        
        stats.TotalMovies = await dbContext.Movies.CountAsync();
        stats.TotalEpisodes = await dbContext.Episodes.CountAsync();
        
        // Calculate unique translated media counts from the TranslationState field
        // This includes all media that has been analyzed and found to have all target languages
        var translatedMovies = await dbContext.Movies
            .Where(m => m.TranslationState == TranslationState.Complete)
            .CountAsync();

        var translatedEpisodes = await dbContext.Episodes
            .Where(e => e.TranslationState == TranslationState.Complete)
            .CountAsync();

        // Update the TranslationsByMediaType with accurate unique counts
        stats.TranslationsByMediaType = new Dictionary<string, int>
        {
            { MediaType.Movie.ToString(), translatedMovies },
            { MediaType.Episode.ToString(), translatedEpisodes }
        };

        // Cache the result with sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(_cacheExpiration)
            .SetPriority(CacheItemPriority.Normal);

        _cache.Set(StatisticsCacheKey, stats, cacheOptions);

        return stats;
    }

public async Task<IEnumerable<DailyStatistics>> GetDailyStatistics(int? days = null)
    {
        var cacheKey = string.Format(DailyStatisticsCacheKey, days ?? 0);
        
        if (_cache.TryGetValue(cacheKey, out List<DailyStatistics>? cachedStats) && cachedStats != null)
        {
            return cachedStats;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var startDate = days.HasValue 
            ? DateTime.UtcNow.Date.AddDays(-days.Value + 1)
            : DateTime.MinValue;
        var stats = await dbContext.DailyStatistics
            .Where(d => d.Date >= startDate)
            .OrderBy(d => d.Date)
            .ToListAsync();

        // Cache the result with sliding expiration
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(_cacheExpiration)
            .SetPriority(CacheItemPriority.Normal);

_cache.Set(cacheKey, stats, cacheOptions);

        return stats;
    }

    public async Task<IEnumerable<HourlyStatistics>> GetHourlyStatistics(DateTime? date = null)
    {
        var targetDate = (date ?? DateTime.UtcNow).Date;
        var cacheKey = string.Format(HourlyStatisticsCacheKey, targetDate);
        
        if (_cache.TryGetValue(cacheKey, out List<HourlyStatistics>? cachedStats) && cachedStats != null)
        {
            return cachedStats;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var startOfDay = targetDate;
        var endOfDay = targetDate.AddDays(1);

        var hourlyCounts = await dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Completed &&
                        r.CompletedAt >= startOfDay &&
                        r.CompletedAt < endOfDay)
            .GroupBy(r => r.CompletedAt!.Value.Hour)
            .Select(g => new HourlyStatistics
            {
                Hour = g.Key,
                TranslationCount = g.Count()
            })
            .ToListAsync();

        var result = Enumerable.Range(0, 24)
            .Select(hour => hourlyCounts.FirstOrDefault(h => h.Hour == hour) ?? new HourlyStatistics
            {
                Hour = hour,
                TranslationCount = 0
            })
            .OrderBy(h => h.Hour)
            .ToList();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(_cacheExpiration)
            .SetPriority(CacheItemPriority.Normal);

        _cache.Set(cacheKey, result, cacheOptions);

        return result;
    }

    private static async Task<Statistics> GetOrCreateStatistics(LingarrDbContext dbContext)
    {
        var stats = await dbContext.Statistics.SingleOrDefaultAsync();
        if (stats == null)
        {
            stats = new Statistics();
            dbContext.Statistics.Add(stats);
            await dbContext.SaveChangesAsync();
        }

        return stats;
    }

    private static async Task<Statistics> GetStatisticsForRead(LingarrDbContext dbContext)
    {
        var stats = await dbContext.Statistics.AsNoTracking().SingleOrDefaultAsync();
        if (stats == null)
        {
            stats = new Statistics();
            dbContext.Statistics.Add(stats);
            await dbContext.SaveChangesAsync();
        }

        return stats;
    }

    public async Task<int> UpdateTranslationStatisticsFromSubtitles(
        TranslationRequest request,
        string serviceType,
        List<SubtitleItem> translatedSubtitles)
    {
        int lineCount = translatedSubtitles.Sum(s => s.Lines.Count);
        int charCount = translatedSubtitles.Sum(s => s.Lines.Sum(l => l.Length));

        return await UpdateTranslationStatisticsInternal(
            request, serviceType, lineCount, charCount);
    }

    public async Task<int> UpdateTranslationStatisticsFromLines(
        TranslationRequest request,
        string serviceType,
        BatchTranslatedLine[] translatedLines)
    {
        int lineCount = translatedLines.Length;
        int charCount = translatedLines.Sum(s => s.Line.Length);

        return await UpdateTranslationStatisticsInternal(
            request, serviceType, lineCount, charCount);
    }

    private async Task<int> UpdateTranslationStatisticsInternal(
        TranslationRequest request,
        string serviceType,
        int totalLines,
        int totalCharacters)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var today = DateTime.UtcNow.Date;

        // Ensure statistics row exists before raw SQL update
        await GetOrCreateStatistics(dbContext);

        // Use raw SQL for atomic updates to prevent race conditions
        // This works for both PostgreSQL and SQLite
        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE statistics SET " +
            "total_lines_translated = total_lines_translated + {0}, " +
            "total_characters_translated = total_characters_translated + {1}, " +
            "total_files_translated = total_files_translated + 1",
            totalLines, totalCharacters);

        // Update service type statistics (requires read-modify-write for JSON field)
        var stats = await GetOrCreateStatistics(dbContext);
        var serviceStats = stats.TranslationsByService;
        serviceStats[serviceType] = serviceStats.GetValueOrDefault(serviceType) + 1;
        stats.TranslationsByService = serviceStats;

        // Update language statistics
        var languageStats = stats.SubtitlesByLanguage;
        languageStats[request.TargetLanguage] = languageStats.GetValueOrDefault(request.TargetLanguage) + 1;
        stats.SubtitlesByLanguage = languageStats;

        // Save JSON field changes
        await dbContext.SaveChangesAsync();

        // Update daily statistics using atomic upsert to prevent race conditions
        // Both PostgreSQL and SQLite 3.24.0+ support ON CONFLICT syntax
        await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO daily_statistics (date, translation_count, created_at, updated_at) " +
            "VALUES ({0}, 1, {1}, {1}) " +
            "ON CONFLICT (date) DO UPDATE SET " +
            "translation_count = daily_statistics.translation_count + 1, " +
            "updated_at = {1}",
            today, DateTime.UtcNow);

        var result = 1; // Return 1 to indicate success

        // Invalidate cache after successful update
        InvalidateCache();

        return result;
    }

    /// <summary>
    /// Invalidates the statistics cache. Called after translation completion.
    /// </summary>
    public void InvalidateCache()
    {
        _cache.Remove(StatisticsCacheKey);
        // Remove all daily statistics cache entries
        for (int i = 1; i <= 365; i++)
        {
            _cache.Remove(string.Format(DailyStatisticsCacheKey, i));
        }
    }
}
