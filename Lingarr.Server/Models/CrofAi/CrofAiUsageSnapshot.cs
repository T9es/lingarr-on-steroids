namespace Lingarr.Server.Models.CrofAi;

public class CrofAiUsageSnapshot
{
    public bool HasApiKey { get; set; }
    public int? UsableRequests { get; set; }
    public decimal? Credits { get; set; }
    public DateTime LastSyncedUtc { get; set; }
    public string? Message { get; set; }
}
