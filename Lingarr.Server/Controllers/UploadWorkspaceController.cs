using System.IO;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.UploadWorkspace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadWorkspaceController : ControllerBase
{
    private const long MaxChunkUploadBytes = 16L * 1024 * 1024;

    private readonly IUploadWorkspaceService _uploadWorkspaceService;

    public UploadWorkspaceController(IUploadWorkspaceService uploadWorkspaceService)
    {
        _uploadWorkspaceService = uploadWorkspaceService;
    }

    [HttpPost("batches")]
    public async Task<ActionResult<UploadBatchResponse>> CreateBatch(
        [FromBody] CreateUploadBatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var batch = await _uploadWorkspaceService.CreateBatchAsync(request, cancellationToken);
            return Ok(ToResponse(batch));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("batches")]
    public async Task<ActionResult<List<UploadBatchResponse>>> GetBatches(CancellationToken cancellationToken)
    {
        var batches = await _uploadWorkspaceService.GetBatchesAsync(cancellationToken);
        return Ok(batches.Select(ToResponse).ToList());
    }

    [HttpGet("batches/{batchId:int}")]
    public async Task<ActionResult<UploadBatchResponse>> GetBatch(int batchId, CancellationToken cancellationToken)
    {
        var batch = await _uploadWorkspaceService.GetBatchAsync(batchId, cancellationToken);
        return batch == null ? NotFound() : Ok(ToResponse(batch));
    }

    [HttpPut("batches/{batchId:int}")]
    public async Task<ActionResult<UploadBatchResponse>> UpdateBatch(
        int batchId,
        [FromBody] UpdateUploadBatchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var batch = await _uploadWorkspaceService.UpdateBatchAsync(batchId, request, cancellationToken);
            return batch == null ? NotFound() : Ok(ToResponse(batch));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("batches/{batchId:int}/files")]
    public async Task<ActionResult<UploadBatchResponse>> UploadFiles(
        int batchId,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var batch = await _uploadWorkspaceService.UploadFilesAsync(batchId, files, cancellationToken);
            return batch == null ? NotFound() : Ok(ToResponse(batch));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("batches/{batchId:int}/files/chunked")]
    public async Task<ActionResult<UploadChunkSessionResponse>> CreateChunkSession(
        int batchId,
        [FromBody] CreateUploadChunkSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _uploadWorkspaceService.CreateChunkSessionAsync(batchId, request, cancellationToken);
            return session == null ? NotFound() : Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("batches/{batchId:int}/files/chunked/{uploadId:guid}/chunks/{chunkIndex:int}")]
    [Consumes("application/octet-stream")]
    [RequestSizeLimit(MaxChunkUploadBytes)]
    public async Task<ActionResult<UploadChunkResponse>> UploadChunk(
        int batchId,
        Guid uploadId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _uploadWorkspaceService.UploadChunkAsync(
                batchId,
                uploadId,
                chunkIndex,
                Request.Body,
                Request.ContentLength,
                cancellationToken);
            return response == null ? NotFound() : Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("batches/{batchId:int}/files/chunked/{uploadId:guid}/complete")]
    public async Task<ActionResult<UploadBatchResponse>> CompleteChunkSession(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        try
        {
            var batch = await _uploadWorkspaceService.CompleteChunkSessionAsync(batchId, uploadId, cancellationToken);
            return batch == null ? NotFound() : Ok(ToResponse(batch));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("batches/{batchId:int}/files/chunked/{uploadId:guid}")]
    public async Task<IActionResult> CancelChunkSession(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        return await _uploadWorkspaceService.CancelChunkSessionAsync(batchId, uploadId, cancellationToken)
            ? Ok()
            : NotFound();
    }

    [HttpPost("batches/{batchId:int}/files/{fileId:int}/reprobe")]
    public async Task<ActionResult<UploadBatchFileResponse>> ReprobeFile(
        int batchId,
        int fileId,
        CancellationToken cancellationToken)
    {
        var file = await _uploadWorkspaceService.ReprobeFileAsync(batchId, fileId, cancellationToken);
        return file == null ? NotFound() : Ok(ToResponse(file));
    }

    [HttpPut("batches/{batchId:int}/files/{fileId:int}")]
    public async Task<ActionResult<UploadBatchFileResponse>> UpdateFile(
        int batchId,
        int fileId,
        [FromBody] UpdateUploadBatchFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await _uploadWorkspaceService.UpdateFileAsync(batchId, fileId, request, cancellationToken);
            return file == null ? NotFound() : Ok(ToResponse(file));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("batches/{batchId:int}/start")]
    public async Task<ActionResult<object>> StartBatch(int batchId, CancellationToken cancellationToken)
    {
        try
        {
            var queuedCount = await _uploadWorkspaceService.StartBatchAsync(batchId, cancellationToken);
            return Ok(new { queuedCount });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("batches/{batchId:int}/cancel")]
    public async Task<IActionResult> CancelBatch(int batchId, CancellationToken cancellationToken)
    {
        return await _uploadWorkspaceService.CancelBatchAsync(batchId, cancellationToken)
            ? Ok()
            : NotFound();
    }

    [HttpGet("batches/{batchId:int}/artifacts")]
    public async Task<ActionResult<List<UploadArtifactResponse>>> GetArtifacts(int batchId, CancellationToken cancellationToken)
    {
        var batch = await _uploadWorkspaceService.GetBatchAsync(batchId, cancellationToken);
        if (batch == null)
        {
            return NotFound();
        }

        var artifacts = batch.Artifacts
            .OrderByDescending(item => item.CreatedAt)
            .Select(ToResponse)
            .ToList();
        return Ok(artifacts);
    }

    [HttpGet("artifacts/{artifactId:int}/download")]
    public async Task<IActionResult> DownloadArtifact(int artifactId, CancellationToken cancellationToken)
    {
        var artifact = await _uploadWorkspaceService.GetArtifactAsync(artifactId, cancellationToken);
        if (artifact == null || !artifact.IsDownloadable)
        {
            return NotFound();
        }

        if (!await _uploadWorkspaceService.IsPathWithinWorkspaceRootAsync(artifact.Path, cancellationToken))
        {
            return NotFound();
        }

        var fullArtifactPath = Path.GetFullPath(artifact.Path);
        if (!System.IO.File.Exists(fullArtifactPath))
        {
            return NotFound();
        }

        var contentType = string.IsNullOrWhiteSpace(artifact.ContentType)
            ? "application/octet-stream"
            : artifact.ContentType;
        var stream = new FileStream(fullArtifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, artifact.FileName);
    }

    [HttpDelete("artifacts/{artifactId:int}")]
    public async Task<IActionResult> DeleteArtifact(int artifactId, CancellationToken cancellationToken)
    {
        return await _uploadWorkspaceService.DeleteArtifactAsync(artifactId, cancellationToken)
            ? Ok()
            : NotFound();
    }

    [HttpDelete("batches/{batchId:int}")]
    public async Task<IActionResult> DeleteBatch(int batchId, CancellationToken cancellationToken)
    {
        return await _uploadWorkspaceService.DeleteBatchAsync(batchId, cancellationToken)
            ? Ok()
            : NotFound();
    }

    private UploadBatchResponse ToResponse(UploadBatch batch)
    {
        return new UploadBatchResponse
        {
            Id = batch.Id,
            Name = batch.Name,
            TargetLanguage = batch.TargetLanguage,
            Status = batch.Status,
            DefaultRemuxEnabled = batch.DefaultRemuxEnabled,
            FileCount = batch.Files.Count,
            CompletedFileCount = batch.Files.Count(item => item.Status == UploadBatchFileStatus.Completed),
            FailedFileCount = batch.Files.Count(item => item.Status == UploadBatchFileStatus.Failed),
            ActiveFileCount = batch.Files.Count(item =>
                item.Status is UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing),
            CreatedAt = batch.CreatedAt,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt,
            ExpiresAt = batch.ExpiresAt,
            FailureReason = batch.FailureReason,
            Files = batch.Files
                .OrderBy(item => item.CreatedAt)
                .Select(ToResponse)
                .ToList(),
            Artifacts = batch.Artifacts
                .OrderByDescending(item => item.CreatedAt)
                .Select(ToResponse)
                .ToList()
        };
    }

    private UploadBatchFileResponse ToResponse(UploadBatchFile file)
    {
        return new UploadBatchFileResponse
        {
            Id = file.Id,
            Title = file.Title,
            OriginalFileName = file.OriginalFileName,
            FileKind = file.FileKind,
            Status = file.Status,
            FileSizeBytes = file.FileSizeBytes,
            DetectedSourceLanguage = file.DetectedSourceLanguage,
            SelectedSourceLanguage = file.SelectedSourceLanguage,
            ExcludeFromTranslation = file.ExcludeFromTranslation,
            EmbedTranslatedSubtitle = file.EmbedTranslatedSubtitle,
            SelectedEmbeddedStreamIndex = file.SelectedEmbeddedStreamIndex,
            SelectedEmbeddedStreamLanguage = file.SelectedEmbeddedStreamLanguage,
            SelectedEmbeddedStreamTitle = file.SelectedEmbeddedStreamTitle,
            SelectedEmbeddedStreamCodec = file.SelectedEmbeddedStreamCodec,
            CurrentTranslationRequestId = file.CurrentTranslationRequestId,
            ProbeCompletedAt = file.ProbeCompletedAt,
            StartedAt = file.StartedAt,
            CompletedAt = file.CompletedAt,
            ProbeError = file.ProbeError,
            LastError = file.LastError,
            SubtitleStreams = file.SubtitleStreams
                .OrderBy(item => item.StreamIndex)
                .Select(item => new UploadBatchFileSubtitleStreamResponse
                {
                    Id = item.Id,
                    StreamIndex = item.StreamIndex,
                    Language = item.Language,
                    Title = item.Title,
                    CodecName = item.CodecName,
                    IsTextBased = item.IsTextBased,
                    IsDefault = item.IsDefault,
                    IsForced = item.IsForced
                })
                .ToList(),
            Artifacts = file.Artifacts
                .OrderByDescending(item => item.CreatedAt)
                .Select(ToResponse)
                .ToList()
        };
    }

    private UploadArtifactResponse ToResponse(UploadArtifact artifact)
    {
        return new UploadArtifactResponse
        {
            Id = artifact.Id,
            UploadBatchFileId = artifact.UploadBatchFileId,
            Kind = artifact.Kind,
            FileName = artifact.FileName,
            FileSizeBytes = artifact.FileSizeBytes,
            ContentType = artifact.ContentType,
            IsDownloadable = artifact.IsDownloadable,
            CreatedAt = artifact.CreatedAt,
            ExpiresAt = artifact.ExpiresAt,
            DownloadUrl = $"/api/UploadWorkspace/artifacts/{artifact.Id}/download"
        };
    }
}
