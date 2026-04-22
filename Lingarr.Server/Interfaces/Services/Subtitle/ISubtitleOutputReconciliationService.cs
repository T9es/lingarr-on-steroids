using Lingarr.Core.Enum;
using Lingarr.Server.Models.Api;

namespace Lingarr.Server.Interfaces.Services.Subtitle;

public interface ISubtitleOutputReconciliationService
{
    Task<SubtitleOutputReconciliationResponse> ReconcileLibraryOutputsAsync(
        CancellationToken cancellationToken = default);

    Task<SubtitleOutputReconciliationResponse> ReconcileMediaOutputsAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);
}
