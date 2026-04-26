using Lingarr.Server.Models.NanoGpt;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface INanoGptUsageService
{
    Task<NanoGptUsageSnapshot> GetUsageSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken);
    Task EnsureUsageAvailableAsync(CancellationToken cancellationToken);
}
