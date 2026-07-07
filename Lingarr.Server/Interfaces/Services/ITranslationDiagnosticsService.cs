using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services;

public interface ITranslationDiagnosticsService
{
    Task<TranslationDiagnosticEvent> RecordAsync(
        TranslationDiagnosticEventRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslationDiagnosticEvent>> GetEventsAsync(
        int pageSize,
        int pageNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslationDiagnosticEvent>> GetForRequestAsync(
        int requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TranslationDiagnosticEvent>> GetForMediaAsync(
        MediaType mediaType,
        int mediaId,
        CancellationToken cancellationToken);

    string CreateQuarantinePath(int translationRequestId, string finalPath);

    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
}
