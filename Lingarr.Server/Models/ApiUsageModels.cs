namespace Lingarr.Server.Models;

/// <summary>
/// Daily usage breakdown for sparkline charts
/// </summary>
public class DailyUsage
{
    public DateTime Date { get; set; }
    public int CallCount { get; set; }
    public int TokenCount { get; set; }
}

/// <summary>
/// Per-service usage statistics
/// </summary>
public class ServiceUsage
{
    public int TotalCalls { get; set; }
    public int TotalTokens { get; set; }
    public double AverageResponseTime { get; set; }
    public int ErrorCount { get; set; }
    public int SuccessRate { get; set; }
    public List<DailyUsage> DailyBreakdown { get; set; } = new();
}

/// <summary>
/// API usage status response
/// </summary>
public class ApiUsageStatus
{
    public int TotalCallsToday { get; set; }
    public int TotalCallsWeek { get; set; }
    public int TotalTokensToday { get; set; }
    public int TotalTokensWeek { get; set; }
    public double AverageResponseTime { get; set; }
    public int ErrorCount { get; set; }
    public int SuccessRate { get; set; }
    public Dictionary<string, ServiceUsage> ByService { get; set; } = new();
    public List<ApiUsageEntry> RecentCalls { get; set; } = new();
}

/// <summary>
/// Individual API usage entry
/// </summary>
public class ApiUsageEntry
{
    public DateTime Timestamp { get; set; }
    public string Service { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
