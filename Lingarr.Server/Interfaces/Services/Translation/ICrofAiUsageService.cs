using Lingarr.Server.Models.CrofAi;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ICrofAiUsageService
{
    Task EnsureRequestAllowedAsync(CancellationToken cancellationToken);
    Task RecordRequestAsync(CancellationToken cancellationToken);
    Task<CrofAiUsageSnapshot> GetUsageSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken);
}
