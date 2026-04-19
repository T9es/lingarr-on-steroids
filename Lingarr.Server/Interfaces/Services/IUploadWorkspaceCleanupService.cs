namespace Lingarr.Server.Interfaces.Services;

public interface IUploadWorkspaceCleanupService
{
    Task<int> CleanupExpiredBatchesAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredArtifactsAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupStaleIntermediatesAsync(CancellationToken cancellationToken = default);
}
