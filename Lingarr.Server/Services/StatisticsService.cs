using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class StatisticsService : IStatisticsService
{
    private readonly LingarrDbContext _dbContext;

    public StatisticsService(
        LingarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Statistics> GetStatistics()
    {
        var stats = await GetOrCreateStatistics();
        
        // Calculate unique translated media counts dynamically from completed translation requests
        // This prevents double-counting when media is re-translated
        var translatedMovies = await _dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Completed && 
                        r.MediaType == MediaType.Movie && 
                        r.MediaId != null)
            .Select(r => r.MediaId)
            .Distinct()
            .CountAsync();

        var translatedEpisodes = await _dbContext.TranslationRequests
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
        var startDate = DateTime.UtcNow.Date.AddDays(-days + 1); // +1 to include today
        var stats = await _dbContext.DailyStatistics
            .Where(d => d.Date >= startDate)
            .OrderBy(d => d.Date)
            .ToListAsync();

        return stats;
    }

    private async Task<Statistics> GetOrCreateStatistics()
    {
        var stats = await _dbContext.Statistics.SingleOrDefaultAsync();
        if (stats == null)
        {
            stats = new Statistics();
            _dbContext.Statistics.Add(stats);
            await _dbContext.SaveChangesAsync();
        }

        return stats;
    }

    private async Task<DailyStatistics> GetOrCreateDailyStatistics(DateTime today)
    {
        var dailyStats = await _dbContext.DailyStatistics
            .Where(d => d.Date >= today)
            .FirstOrDefaultAsync();

        if (dailyStats == null)
        {
            dailyStats = new DailyStatistics { Date = today };
            _dbContext.DailyStatistics.Add(dailyStats);
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
        var stats = await GetOrCreateStatistics();
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
        var dailyStats = await GetOrCreateDailyStatistics(today);
        dailyStats.TranslationCount++;

        return await _dbContext.SaveChangesAsync();
    }
}