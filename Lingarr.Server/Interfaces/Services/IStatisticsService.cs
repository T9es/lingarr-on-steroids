using Lingarr.Core.Entities;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Interfaces.Services;

public interface IStatisticsService
{
    Task<Statistics> GetStatistics();
    Task<IEnumerable<DailyStatistics>> GetDailyStatistics(int days = 30);
    Task<IEnumerable<HourlyStatistics>> GetHourlyStatistics(DateTime? date = null);
    Task<int> UpdateTranslationStatisticsFromSubtitles(
        TranslationRequest request,
        string serviceType,
        List<SubtitleItem> translatedSubtitles);
    Task<int> UpdateTranslationStatisticsFromLines(
        TranslationRequest request, 
        string serviceType, 
        BatchTranslatedLine[] translatedLines);
    
    /// <summary>
    /// Invalidates the statistics cache. Called after translation completion.
    /// </summary>
    void InvalidateCache();
}