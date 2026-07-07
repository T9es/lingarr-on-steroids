using System.IO;
using Lingarr.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace Lingarr.Server.Interfaces.Services;

public interface IUploadWorkspaceService
{
    Task<UploadBatch> CreateBatchAsync(
        Models.UploadWorkspace.CreateUploadBatchRequest request,
        CancellationToken cancellationToken = default);
    Task<List<UploadBatch>> GetBatchesAsync(CancellationToken cancellationToken = default);
    Task<UploadBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<UploadBatch?> UpdateBatchAsync(
        int batchId,
        Models.UploadWorkspace.UpdateUploadBatchRequest request,
        CancellationToken cancellationToken = default);
    Task<UploadBatch?> UploadFilesAsync(
        int batchId,
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken = default);
    Task<Models.UploadWorkspace.UploadChunkSessionResponse?> CreateChunkSessionAsync(
        int batchId,
        Models.UploadWorkspace.CreateUploadChunkSessionRequest request,
        CancellationToken cancellationToken = default);
    Task<Models.UploadWorkspace.UploadChunkResponse?> UploadChunkAsync(
        int batchId,
        Guid uploadId,
        int chunkIndex,
        Stream chunkStream,
        long? contentLength,
        CancellationToken cancellationToken = default);
    Task<UploadBatch?> CompleteChunkSessionAsync(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken = default);
    Task<bool> CancelChunkSessionAsync(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken = default);
    Task<UploadBatchFile?> ReprobeFileAsync(int batchId, int fileId, CancellationToken cancellationToken = default);
    Task<UploadBatchFile?> UpdateFileAsync(
        int batchId,
        int fileId,
        Models.UploadWorkspace.UpdateUploadBatchFileRequest request,
        CancellationToken cancellationToken = default);
    Task<int> StartBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<bool> CancelBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<bool> DeleteBatchAsync(int batchId, CancellationToken cancellationToken = default);
    Task<UploadArtifact?> GetArtifactAsync(int artifactId, CancellationToken cancellationToken = default);
    Task<bool> IsPathWithinWorkspaceRootAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> DeleteArtifactAsync(int artifactId, CancellationToken cancellationToken = default);
    Task<string?> PrepareSubtitleForRequestAsync(TranslationRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetOutputPathsAsync(
        TranslationRequest request,
        string targetLanguage,
        string subtitleTag,
        string subtitleTagShort,
        string outputFormat,
        CancellationToken cancellationToken = default);
    Task HandleRequestCompletedAsync(
        TranslationRequest request,
        IReadOnlyCollection<string> outputPaths,
        CancellationToken cancellationToken = default);
    Task HandleRequestFailedAsync(
        TranslationRequest request,
        string failureMessage,
        CancellationToken cancellationToken = default);
    Task HandleRequestCancelledAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
