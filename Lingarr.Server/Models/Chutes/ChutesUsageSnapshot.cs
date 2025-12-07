namespace Lingarr.Server.Models.Chutes;

public class ChutesUsageSnapshot
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Plan { get; set; }
    public string? ModelId { get; set; }
    public string? ChuteId { get; set; }
    public int RequestsUsed { get; set; }
    public int RemoteRequestsUsed { get; set; }
    public int AllowedRequestsPerDay { get; set; }
    public int? PlanRequestsPerDay { get; set; }
    public int? OverrideRequestsPerDay { get; set; }
    public DateTime? ResetAt { get; set; }
    public DateTime LastSyncedUtc { get; set; }
    public bool HasApiKey { get; set; }
    public string? Message { get; set; }

    public int RequestsRemaining => Math.Max(AllowedRequestsPerDay - RequestsUsed, 0);

    public bool IsLimitReached => AllowedRequestsPerDay > 0 && RequestsUsed >= AllowedRequestsPerDay;
}
