using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lingarr.Server.Services;

public class StatisticsService : IStatisticsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public StatisticsService(
        IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<Statistics> GetStatistics()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var stats = await GetOrCreateStatistics(dbContext);
        
        stats.TotalMovies = await dbContext.Movies.CountAsync();
        stats.TotalEpisodes = await dbContext.Episodes.CountAsync();
        
        // Calculate unique translated media counts dynamically from completed translation requests
        // This prevents double-counting when media is re-translated
        var translatedMovies = await dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Completed && 
                        r.MediaType == MediaType.Movie && 
                        r.MediaId != null)
            .Select(r => r.MediaId)
            .Distinct()
            .CountAsync();

        var translatedEpisodes = await dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Completed && 
                        r.MediaType == MediaType.Episode && 
                        r.MediaId != null)
            .Select(r => r.MediaId)
            .Distinct()
            .CountAsync();

        // Update the TranslationsByMediaType with accurate unique counts
        stats.TranslationsByMediaType = new Dictionary<string, int>
        {
            { MediaType.Movie.ToString(), translatedMovies },
            { MediaType.Episode.ToString(), translatedEpisodes }
        };

        return stats;
    }

    public async Task<IEnumerable<DailyStatistics>> GetDailyStatistics(int days = 30)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        var startDate = DateTime.UtcNow.Date.AddDays(-days + 1); // +1 to include today
        var stats = await dbContext.DailyStatistics
            .Where(d => d.Date >= startDate)
            .OrderBy(d => d.Date)
            .ToListAsync();

        return stats;
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

    private static async Task<DailyStatistics> GetOrCreateDailyStatistics(
        LingarrDbContext dbContext,
        DateTime today)
    {
        var dailyStats = await dbContext.DailyStatistics
            .Where(d => d.Date >= today)
            .FirstOrDefaultAsync();

        if (dailyStats == null)
        {
            dailyStats = new DailyStatistics { Date = today };
            dbContext.DailyStatistics.Add(dailyStats);
        }

        return dailyStats;
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

        var stats = await GetOrCreateStatistics(dbContext);
        var today = DateTime.UtcNow.Date;

        // Update total counts
        stats.TotalLinesTranslated += totalLines;
        stats.TotalCharactersTranslated += totalCharacters;
        stats.TotalFilesTranslated++;

        // NOTE: TranslationsByMediaType is now calculated dynamically in GetStatistics()
        // to count unique media items, not translation operations (prevents double-counting)

        // Update service type statistics
        var serviceStats = stats.TranslationsByService;
        serviceStats[serviceType] = serviceStats.GetValueOrDefault(serviceType) + 1;
        stats.TranslationsByService = serviceStats;

        // Update language statistics
        var languageStats = stats.SubtitlesByLanguage;
        languageStats[request.TargetLanguage] = languageStats.GetValueOrDefault(request.TargetLanguage) + 1;
        stats.SubtitlesByLanguage = languageStats;

        // Update daily statistics
        var dailyStats = await GetOrCreateDailyStatistics(dbContext, today);
        dailyStats.TranslationCount++;

        return await dbContext.SaveChangesAsync();
    }
}
