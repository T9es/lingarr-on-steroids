namespace Lingarr.Server.Models;

/// <summary>
/// Hourly translation statistics for a single day
/// </summary>
public class HourlyStatistics
{
    /// <summary>
    /// Hour of day (0-23)
    /// </summary>
    public int Hour { get; set; }
    
    /// <summary>
    /// Number of translations completed in this hour
    /// </summary>
    public int TranslationCount { get; set; }
}