namespace Lingarr.Server.Models.NanoGpt;

public class NanoGptUsageSnapshot
{
    public bool Active { get; set; }
    public string? State { get; set; }
    public NanoGptUsageWindow Daily { get; set; } = new();
    public NanoGptUsageWindow Monthly { get; set; } = new();
    public NanoGptUsageWindow DailyImages { get; set; } = new();
    public NanoGptUsageWindow WeeklyTokens { get; set; } = new();
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime LastSyncedUtc { get; set; } = DateTime.UtcNow;
    public bool HasApiKey { get; set; }
    public string? Message { get; set; }
}

public class NanoGptUsageWindow
{
    public long? Limit { get; set; }
    public long Used { get; set; }
    public long? Remaining { get; set; }
    public double PercentUsed { get; set; }
    public DateTime? ResetAt { get; set; }
}

public class NanoGptReserveSettings
{
    public long DailyUnitReserve { get; set; }
    public long MonthlyUnitReserve { get; set; }
    public long TokenReserve { get; set; }
}

public class NanoGptReserveResult
{
    public bool IsBlocked { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? ResetAt { get; set; }
}
