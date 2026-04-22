using Lingarr.Server.Models.Api;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleOutputReconciliationService
{
    Task<SubtitleOutputReconciliationResponse> ReconcileLibraryOutputsAsync(
        CancellationToken cancellationToken = default);
}
