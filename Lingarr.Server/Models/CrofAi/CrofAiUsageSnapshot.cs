using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.CrofAi;

public class CrofAiUsageSnapshot
{
    public bool HasApiKey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? UsableRequests { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? Credits { get; set; }
    public DateTime LastSyncedUtc { get; set; }
    public string? Message { get; set; }
}
