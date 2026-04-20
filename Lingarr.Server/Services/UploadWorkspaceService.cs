using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.UploadWorkspace;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class UploadWorkspaceService : IUploadWorkspaceService, IUploadWorkspaceCleanupService
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt"];
    private static readonly string[] MediaExtensions = [".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv"];
    private const int DefaultChunkSizeBytes = 8 * 1024 * 1024;
    private const int MaxChunkSizeBytes = 16 * 1024 * 1024;
    private const int ChunkFileBufferSize = 81920;
    private const string SourceLanguageMatchesTargetMessage =
        "Source language cannot match the batch target language. Choose a different source language.";
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<int, RefCountedSemaphore> BatchIngestionLocks = new();
    private static readonly ConcurrentDictionary<Guid, RefCountedSemaphore> ChunkSessionLocks = new();
    private static readonly object CachedLockGate = new();
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleExtractionService _subtitleExtractionService;
    private readonly Lazy<ITranslationRequestService> _translationRequestServiceLazy;
    private readonly ILogger<UploadWorkspaceService> _logger;

    public UploadWorkspaceService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService subtitleExtractionService,
        Lazy<ITranslationRequestService> translationRequestServiceLazy,
        ILogger<UploadWorkspaceService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _subtitleExtractionService = subtitleExtractionService;
        _translationRequestServiceLazy = translationRequestServiceLazy;
        _logger = logger;
    }

    public async Task<UploadBatch> CreateBatchAsync(
        CreateUploadBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);
        var batch = new UploadBatch
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"Batch {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
                : request.Name.Trim(),
            TargetLanguage = NormalizeLanguage(request.TargetLanguage)
                ?? throw new InvalidOperationException("Target language is required."),
            StoragePath = string.Empty,
            DefaultRemuxEnabled = request.DefaultRemuxEnabled,
            Status = UploadBatchStatus.Draft,
            ExpiresAt = DateTime.UtcNow.AddDays(retentionDays)
        };

        _dbContext.UploadBatches.Add(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        batch.StoragePath = Path.Combine(workspaceRoot, $"batch-{batch.Id:D6}");
        EnsureBatchDirectories(batch.StoragePath);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await LoadBatchAsync(batch.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to load created upload batch.");
    }

    public async Task<List<UploadBatch>> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.UploadBatches
            .Include(batch => batch.Files)
            .ThenInclude(file => file.SubtitleStreams)
            .Include(batch => batch.Files)
            .ThenInclude(file => file.Artifacts)
            .Include(batch => batch.Artifacts)
            .OrderByDescending(batch => batch.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<UploadBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        return await LoadBatchAsync(batchId, cancellationToken);
    }

    public async Task<UploadBatch?> UpdateBatchAsync(
        int batchId,
        UpdateUploadBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return null;
        }

        batch.Name = request.Name.Trim();
        batch.TargetLanguage = NormalizeLanguage(request.TargetLanguage)
            ?? throw new InvalidOperationException("Target language is required.");
        batch.DefaultRemuxEnabled = request.DefaultRemuxEnabled;

        foreach (var file in batch.Files)
        {
            if (file.FileKind == UploadBatchFileKind.Media &&
                file.Status is (UploadBatchFileStatus.Uploaded or
                    UploadBatchFileStatus.NeedsConfiguration or
                    UploadBatchFileStatus.Ready or
                    UploadBatchFileStatus.Failed or
                    UploadBatchFileStatus.Cancelled))
            {
                file.EmbedTranslatedSubtitle = request.DefaultRemuxEnabled;
            }

            if (file.Status is UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing or UploadBatchFileStatus.Completed)
            {
                continue;
            }

            UpdateFileStatusForConfiguration(file, batch.TargetLanguage);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(batchId, cancellationToken);
        return await LoadBatchAsync(batchId, cancellationToken);
    }

    public async Task<UploadBatch?> UploadFilesAsync(
        int batchId,
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("At least one file is required.");
        }

        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);

        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return null;
        }

        if (batch.Status == UploadBatchStatus.Processing)
        {
            throw new InvalidOperationException("Cannot upload new files while a batch is processing.");
        }

        await ValidateIncomingFilesAsync(batch, files, cancellationToken);
        EnsureBatchDirectories(batch.StoragePath);

        var originalsDirectory = GetOriginalsDirectory(batch.StoragePath);

        foreach (var formFile in files)
        {
            var sanitizedOriginalFileName = SanitizeFileName(formFile.FileName);
            var reservedFile = ReserveUniqueFile(originalsDirectory, sanitizedOriginalFileName);
            var destinationPath = reservedFile.FullPath;

            await using (reservedFile.Stream)
            {
                await formFile.CopyToAsync(reservedFile.Stream, cancellationToken);
            }

            await IngestStoredFileAsync(
                batch,
                sanitizedOriginalFileName,
                destinationPath,
                formFile.Length,
                formFile.ContentType,
                cancellationToken);
        }

        await RefreshBatchStatusAsync(batch.Id, cancellationToken);
        return await LoadBatchAsync(batch.Id, cancellationToken);
    }

    public async Task<UploadChunkSessionResponse?> CreateChunkSessionAsync(
        int batchId,
        CreateUploadChunkSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);

        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return null;
        }

        if (batch.Status == UploadBatchStatus.Processing)
        {
            throw new InvalidOperationException("Cannot upload new files while a batch is processing.");
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new InvalidOperationException("File name is required.");
        }

        var sanitizedFileName = SanitizeFileName(request.FileName);
        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (!IsAllowedUploadExtension(extension))
        {
            throw new InvalidOperationException($"Unsupported upload file type: {sanitizedFileName}");
        }

        if (request.FileSizeBytes <= 0)
        {
            throw new InvalidOperationException("File size must be greater than zero.");
        }

        await EnsureBatchHasCapacityAsync(batch.Files.Count, 1, cancellationToken);

        var maxFileSizeBytes = await GetMaxFileSizeBytesAsync(cancellationToken);
        if (request.FileSizeBytes > maxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"{sanitizedFileName} exceeds the configured file-size limit of {maxFileSizeBytes} bytes.");
        }

        EnsureEnoughDiskSpace(batch.StoragePath, request.FileSizeBytes);
        EnsureBatchDirectories(batch.StoragePath);

        var uploadId = Guid.NewGuid();
        var sessionDirectory = GetIncomingSessionDirectory(batch.StoragePath, uploadId);
        EnsurePathWithinBatchStorageRoot(sessionDirectory, batch.StoragePath, "chunk upload session");
        Directory.CreateDirectory(sessionDirectory);

        var manifest = new UploadChunkManifest
        {
            UploadId = uploadId,
            BatchId = batch.Id,
            FileName = sanitizedFileName,
            FileSizeBytes = request.FileSizeBytes,
            ContentType = request.ContentType,
            LastModifiedUtc = request.LastModifiedUtc,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var manifestPath = GetChunkManifestPath(sessionDirectory);
        await WriteChunkManifestAsync(manifestPath, manifest, cancellationToken);

        return ToChunkSessionResponse(manifest);
    }

    public async Task<UploadChunkResponse?> UploadChunkAsync(
        int batchId,
        Guid uploadId,
        int chunkIndex,
        Stream chunkStream,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        if (chunkIndex < 0)
        {
            throw new InvalidOperationException("Chunk index must be zero or greater.");
        }

        if (contentLength.HasValue && contentLength.Value > MaxChunkSizeBytes)
        {
            throw new InvalidOperationException(
                $"Chunk size exceeds the maximum allowed size of {MaxChunkSizeBytes} bytes.");
        }

        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);
        using var chunkSessionLock = await AcquireCachedLockAsync(
            ChunkSessionLocks,
            uploadId,
            cancellationToken);

        var session = await LoadChunkSessionContextAsync(batchId, uploadId, cancellationToken);
        if (session == null)
        {
            return null;
        }

        if (session.Batch.Status == UploadBatchStatus.Processing)
        {
            throw new InvalidOperationException("Cannot upload chunks while a batch is processing.");
        }

        var highestExistingChunkIndex = session.Manifest.ChunkSizes.Count == 0
            ? -1
            : session.Manifest.ChunkSizes.Keys.Max();
        if (!session.Manifest.ChunkSizes.ContainsKey(chunkIndex) && chunkIndex > highestExistingChunkIndex + 1)
        {
            throw new InvalidOperationException("Chunk indices must be uploaded in contiguous order.");
        }

        var chunkPath = GetChunkPath(session.SessionDirectory, chunkIndex);
        EnsurePathWithinBatchStorageRoot(chunkPath, session.Batch.StoragePath, "chunk file");
        var temporaryChunkPath = $"{chunkPath}.{Guid.NewGuid():N}.tmp";
        EnsurePathWithinBatchStorageRoot(temporaryChunkPath, session.Batch.StoragePath, "temporary chunk file");

        long bytesWritten;
        try
        {
            await using (var chunkFileStream = new FileStream(
                             temporaryChunkPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             ChunkFileBufferSize,
                             FileOptions.Asynchronous))
            {
                bytesWritten = await CopyStreamWithLimitAsync(
                    chunkStream,
                    chunkFileStream,
                    MaxChunkSizeBytes,
                    cancellationToken);
            }

            if (bytesWritten <= 0)
            {
                throw new InvalidOperationException("Chunk payload is empty.");
            }

            var existingChunkSize = session.Manifest.ChunkSizes.GetValueOrDefault(chunkIndex);
            var uploadedBytesAfterChunk = session.Manifest.ChunkSizes.Values.Sum() -
                                          existingChunkSize +
                                          bytesWritten;
            if (uploadedBytesAfterChunk > session.Manifest.FileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Uploaded chunk data exceeds the expected file size of {session.Manifest.FileSizeBytes} bytes.");
            }

            File.Move(temporaryChunkPath, chunkPath, overwrite: true);

            session.Manifest.ChunkSizes[chunkIndex] = bytesWritten;
            session.Manifest.UpdatedAtUtc = DateTime.UtcNow;
            await WriteChunkManifestAsync(session.ManifestPath, session.Manifest, cancellationToken);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryChunkPath))
                {
                    File.Delete(temporaryChunkPath);
                }
            }
            catch
            {
            }

            throw;
        }

        var uploadedBytes = session.Manifest.ChunkSizes.Values.Sum();
        var uploadedChunkCount = session.Manifest.ChunkSizes.Count;
        var contiguousChunkCount = session.Manifest.ChunkSizes.Count == 0
            ? 0
            : session.Manifest.ChunkSizes.Keys.Max() + 1;
        var isComplete = uploadedChunkCount == contiguousChunkCount &&
                         uploadedBytes == session.Manifest.FileSizeBytes;

        return new UploadChunkResponse
        {
            UploadId = uploadId,
            ChunkIndex = chunkIndex,
            ChunkSizeBytes = bytesWritten,
            UploadedChunkCount = uploadedChunkCount,
            UploadedBytes = uploadedBytes,
            FileSizeBytes = session.Manifest.FileSizeBytes,
            IsComplete = isComplete
        };
    }

    public async Task<UploadBatch?> CompleteChunkSessionAsync(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);
        using var chunkSessionLock = await AcquireCachedLockAsync(
            ChunkSessionLocks,
            uploadId,
            cancellationToken);

        var session = await LoadChunkSessionContextAsync(batchId, uploadId, cancellationToken);
        if (session == null)
        {
            return null;
        }

        if (session.Batch.Status == UploadBatchStatus.Processing)
        {
            throw new InvalidOperationException("Cannot complete chunk upload while a batch is processing.");
        }

        var currentFileCount = await _dbContext.UploadBatchFiles
            .CountAsync(item => item.UploadBatchId == batchId, cancellationToken);
        await EnsureBatchHasCapacityAsync(currentFileCount, 1, cancellationToken);

        if (session.Manifest.ChunkSizes.Count == 0)
        {
            throw new InvalidOperationException("No chunks were uploaded for this session.");
        }

        var maxChunkIndex = session.Manifest.ChunkSizes.Keys.Max();
        var chunkPaths = new List<string>(maxChunkIndex + 1);
        long totalBytes = 0;

        for (var chunkIndex = 0; chunkIndex <= maxChunkIndex; chunkIndex++)
        {
            if (!session.Manifest.ChunkSizes.TryGetValue(chunkIndex, out var expectedChunkSize))
            {
                throw new InvalidOperationException($"Missing chunk {chunkIndex}.");
            }

            var chunkPath = GetChunkPath(session.SessionDirectory, chunkIndex);
            if (!File.Exists(chunkPath))
            {
                throw new InvalidOperationException($"Missing chunk file for chunk {chunkIndex}.");
            }

            var chunkSize = new FileInfo(chunkPath).Length;
            if (chunkSize != expectedChunkSize)
            {
                throw new InvalidOperationException(
                    $"Chunk {chunkIndex} size mismatch. Expected {expectedChunkSize} bytes but found {chunkSize} bytes.");
            }

            chunkPaths.Add(chunkPath);
            totalBytes += chunkSize;
        }

        if (totalBytes != session.Manifest.FileSizeBytes)
        {
            throw new InvalidOperationException(
                $"Uploaded chunk data does not match the expected file size of {session.Manifest.FileSizeBytes} bytes.");
        }

        var originalsDirectory = GetOriginalsDirectory(session.Batch.StoragePath);
        Directory.CreateDirectory(originalsDirectory);

        var reservedFile = ReserveUniqueFile(originalsDirectory, session.Manifest.FileName);
        var destinationPath = reservedFile.FullPath;
        try
        {
            await using (reservedFile.Stream)
            {
                foreach (var chunkPath in chunkPaths)
                {
                    await using var chunkStream = new FileStream(
                        chunkPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        ChunkFileBufferSize,
                        FileOptions.Asynchronous);
                    await chunkStream.CopyToAsync(reservedFile.Stream, ChunkFileBufferSize, cancellationToken);
                }

                await reservedFile.Stream.FlushAsync(cancellationToken);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
            }
            catch
            {
            }

            throw;
        }

        await IngestStoredFileAsync(
            session.Batch,
            session.Manifest.FileName,
            destinationPath,
            session.Manifest.FileSizeBytes,
            session.Manifest.ContentType,
            cancellationToken);

        DeleteDirectorySafe(session.SessionDirectory, session.Batch.StoragePath);
        await RefreshBatchStatusAsync(batchId, cancellationToken);
        return await LoadBatchAsync(batchId, cancellationToken);
    }

    public async Task<bool> CancelChunkSessionAsync(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);
        using var chunkSessionLock = await AcquireCachedLockAsync(
            ChunkSessionLocks,
            uploadId,
            cancellationToken);

        var batch = await _dbContext.UploadBatches
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return false;
        }

        var sessionDirectory = GetIncomingSessionDirectory(batch.StoragePath, uploadId);
        if (!Directory.Exists(sessionDirectory))
        {
            return false;
        }

        DeleteDirectorySafe(sessionDirectory, batch.StoragePath);
        return true;
    }

    public async Task<UploadBatchFile?> ReprobeFileAsync(
        int batchId,
        int fileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.SubtitleStreams)
            .FirstOrDefaultAsync(item => item.Id == fileId && item.UploadBatchId == batchId, cancellationToken);
        if (file == null)
        {
            return null;
        }

        await ProbeFileInternalAsync(file.Id, cancellationToken);
        await RefreshBatchStatusAsync(batchId, cancellationToken);

        return await _dbContext.UploadBatchFiles
            .Include(item => item.SubtitleStreams)
            .Include(item => item.Artifacts)
            .FirstOrDefaultAsync(item => item.Id == fileId, cancellationToken);
    }

    public async Task<UploadBatchFile?> UpdateFileAsync(
        int batchId,
        int fileId,
        UpdateUploadBatchFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.UploadBatch)
            .Include(item => item.SubtitleStreams)
            .Include(item => item.Artifacts)
            .FirstOrDefaultAsync(item => item.Id == fileId && item.UploadBatchId == batchId, cancellationToken);
        if (file == null)
        {
            return null;
        }

        if (file.Status is UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing)
        {
            throw new InvalidOperationException("Cannot edit a file while it is queued or processing.");
        }

        file.ExcludeFromTranslation = request.ExcludeFromTranslation;
        file.EmbedTranslatedSubtitle = file.FileKind == UploadBatchFileKind.Media && request.EmbedTranslatedSubtitle;
        file.SelectedSourceLanguage = SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(
            request.SelectedSourceLanguage,
            out var normalizedRequestedLanguage)
            ? normalizedRequestedLanguage
            : NormalizeLanguage(request.SelectedSourceLanguage);

        if (file.FileKind == UploadBatchFileKind.Media)
        {
            var hasSelectedEmbeddedStreamIndex = request.TryGetSelectedEmbeddedStreamIndex(out var selectedEmbeddedStreamIndex);
            if (hasSelectedEmbeddedStreamIndex)
            {
                if (selectedEmbeddedStreamIndex.HasValue)
                {
                    var stream = file.SubtitleStreams.FirstOrDefault(item =>
                        item.StreamIndex == selectedEmbeddedStreamIndex.Value &&
                        item.IsTextBased);
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Selected subtitle stream was not found.");
                    }

                    file.SelectedEmbeddedStreamIndex = stream.StreamIndex;
                    file.SelectedEmbeddedStreamLanguage = NormalizeLanguage(stream.Language);
                    file.SelectedEmbeddedStreamTitle = stream.Title;
                    file.SelectedEmbeddedStreamCodec = stream.CodecName;
                    file.SelectedSourceLanguage ??= NormalizeLanguage(stream.Language);
                }
                else
                {
                    file.SelectedEmbeddedStreamIndex = null;
                    file.SelectedEmbeddedStreamLanguage = null;
                    file.SelectedEmbeddedStreamTitle = null;
                    file.SelectedEmbeddedStreamCodec = null;
                }
            }
        }

        UpdateFileStatusForConfiguration(file);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(batchId, cancellationToken);

        return file;
    }

    public async Task<int> StartBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        var queuedCount = await strategy.ExecuteAsync(async () =>
        {
            var batch = await _dbContext.UploadBatches
                .Include(item => item.Files)
                .ThenInclude(file => file.SubtitleStreams)
                .Include(item => item.Files)
                .ThenInclude(file => file.Artifacts)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
            if (batch == null)
            {
                return 0;
            }

            if (batch.Status == UploadBatchStatus.Processing)
            {
                throw new InvalidOperationException("Upload batch is already processing.");
            }

            var filesToQueue = batch.Files
                .Where(item => !item.ExcludeFromTranslation && CanQueueFile(item))
                .ToList();

            foreach (var file in filesToQueue)
            {
                ValidateQueuePrerequisites(file);
            }

            await using var transaction = _dbContext.Database.IsRelational()
                ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                var requestIdsByFileId = new Dictionary<int, int>(filesToQueue.Count);

                foreach (var file in filesToQueue)
                {
                    var translateRequest = new TranslateAbleSubtitle
                    {
                        MediaId = file.Id,
                        WorkloadKind = TranslationWorkloadKind.Upload,
                        UploadBatchFileId = file.Id,
                        SubtitlePath = file.FileKind == UploadBatchFileKind.Subtitle ? file.StoredPath : null,
                        SourceLanguage = GetSourceLanguageForQueue(file),
                        TargetLanguage = batch.TargetLanguage,
                        MediaType = MediaType.Movie,
                        SubtitleFormat = file.FileKind == UploadBatchFileKind.Subtitle
                            ? Path.GetExtension(file.StoredPath)
                            : file.SelectedEmbeddedStreamCodec
                    };

                    var requestId = await _translationRequestServiceLazy.Value.CreateRequest(
                        translateRequest,
                        forcePriority: true);

                    if (requestId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Failed to queue translation request for upload file {file.OriginalFileName}.");
                    }

                    requestIdsByFileId[file.Id] = requestId;
                }

                foreach (var file in filesToQueue)
                {
                    await DeleteGeneratedArtifactsForFileAsync(file, cancellationToken);

                    file.LastError = null;
                    file.ProbeError = null;
                    file.Status = UploadBatchFileStatus.Queued;
                    file.StartedAt = null;
                    file.CompletedAt = null;
                    file.CurrentTranslationRequestId = requestIdsByFileId[file.Id];
                }

                batch.StartedAt ??= DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return filesToQueue.Count;
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });

        await RefreshBatchStatusAsync(batchId, cancellationToken);
        return queuedCount;
    }

    public async Task<bool> CancelBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return false;
        }

        foreach (var file in batch.Files.Where(item =>
                     item.Status is (UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing)))
        {
            if (!file.CurrentTranslationRequestId.HasValue)
            {
                continue;
            }

            await _translationRequestServiceLazy.Value.CancelTranslationRequest(new TranslationRequest
            {
                Id = file.CurrentTranslationRequestId.Value,
                Title = file.Title,
                SourceLanguage = file.SelectedSourceLanguage ?? file.DetectedSourceLanguage ?? string.Empty,
                TargetLanguage = batch.TargetLanguage,
                MediaType = MediaType.Movie,
                Status = TranslationStatus.Pending
            });

            file.Status = UploadBatchFileStatus.Cancelled;
            file.CompletedAt = DateTime.UtcNow;
            file.LastError ??= "Cancelled by user.";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(batchId, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        using var batchIngestionLock = await AcquireCachedLockAsync(
            BatchIngestionLocks,
            batchId,
            cancellationToken);

        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return false;
        }

        if (batch.Status == UploadBatchStatus.Processing)
        {
            await CancelBatchAsync(batchId, cancellationToken);
        }

        DeleteDirectorySafe(batch.StoragePath, await GetWorkspaceRootAsync(cancellationToken));
        _dbContext.UploadBatches.Remove(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<UploadArtifact?> GetArtifactAsync(int artifactId, CancellationToken cancellationToken = default)
    {
        return _dbContext.UploadArtifacts
            .FirstOrDefaultAsync(item => item.Id == artifactId, cancellationToken);
    }

    public async Task<bool> IsPathWithinWorkspaceRootAsync(string path, CancellationToken cancellationToken = default)
    {
        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);
        return IsPathWithinWorkspaceRoot(path, workspaceRoot);
    }

    public async Task<bool> DeleteArtifactAsync(int artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await _dbContext.UploadArtifacts
            .FirstOrDefaultAsync(item => item.Id == artifactId, cancellationToken);
        if (artifact == null)
        {
            return false;
        }

        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);
        DeleteFileSafe(artifact.Path, workspaceRoot);

        _dbContext.UploadArtifacts.Remove(artifact);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (artifact.UploadBatchId != 0)
        {
            await RefreshBatchStatusAsync(artifact.UploadBatchId, cancellationToken);
        }

        return true;
    }

    public async Task<string?> PrepareSubtitleForRequestAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.UploadBatchFileId.HasValue)
        {
            return null;
        }

        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.UploadBatch)
            .Include(item => item.SubtitleStreams)
            .FirstOrDefaultAsync(item => item.Id == request.UploadBatchFileId.Value, cancellationToken);
        if (file == null)
        {
            return null;
        }

        file.Status = UploadBatchFileStatus.Processing;
        file.StartedAt ??= DateTime.UtcNow;

        if (file.FileKind == UploadBatchFileKind.Subtitle)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return File.Exists(file.StoredPath) ? file.StoredPath : null;
        }

        if (!file.SelectedEmbeddedStreamIndex.HasValue || string.IsNullOrWhiteSpace(file.SelectedEmbeddedStreamCodec))
        {
            file.Status = UploadBatchFileStatus.Failed;
            file.LastError = "No embedded subtitle stream is selected for this media upload.";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var extractedDirectory = GetExtractedDirectory(file.UploadBatch.StoragePath);
        Directory.CreateDirectory(extractedDirectory);

        var extractedPath = await _subtitleExtractionService.ExtractSubtitle(
            file.StoredPath,
            file.SelectedEmbeddedStreamIndex.Value,
            extractedDirectory,
            file.SelectedEmbeddedStreamCodec,
            file.SelectedEmbeddedStreamLanguage);

        if (string.IsNullOrWhiteSpace(extractedPath))
        {
            file.Status = UploadBatchFileStatus.Failed;
            file.LastError = "Failed to extract the selected embedded subtitle stream.";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        await UpsertArtifactAsync(
            file.UploadBatch,
            file,
            UploadArtifactKind.ExtractedSubtitle,
            extractedPath,
            isDownloadable: false,
            cancellationToken: cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return extractedPath;
    }

    public async Task<IEnumerable<string>> GetOutputPathsAsync(
        TranslationRequest request,
        string targetLanguage,
        string subtitleTag,
        string subtitleTagShort,
        string outputFormat,
        CancellationToken cancellationToken = default)
    {
        if (!request.UploadBatchFileId.HasValue)
        {
            return [];
        }

        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.UploadBatch)
            .FirstOrDefaultAsync(item => item.Id == request.UploadBatchFileId.Value, cancellationToken);
        if (file == null)
        {
            return [];
        }

        var translatedDirectory = GetTranslatedDirectory(file.UploadBatch.StoragePath);
        Directory.CreateDirectory(translatedDirectory);
        var outputSeedPath = Path.Combine(translatedDirectory, SanitizeFileName(file.OriginalFileName));
        EnsurePathWithinBatchStorageRoot(outputSeedPath, file.UploadBatch.StoragePath, "output seed");

        var outputPaths = _subtitleService.CreateFallbackPaths(
            outputSeedPath,
            targetLanguage,
            subtitleTag,
            subtitleTagShort,
            outputFormat)
            .Select(Path.GetFullPath)
            .ToList();

        foreach (var outputPath in outputPaths)
        {
            EnsurePathWithinBatchStorageRoot(outputPath, file.UploadBatch.StoragePath, "translated output");
        }

        return outputPaths;
    }

    public async Task HandleRequestCompletedAsync(
        TranslationRequest request,
        IReadOnlyCollection<string> outputPaths,
        CancellationToken cancellationToken = default)
    {
        if (!request.UploadBatchFileId.HasValue)
        {
            return;
        }

        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.UploadBatch)
            .FirstOrDefaultAsync(item => item.Id == request.UploadBatchFileId.Value, cancellationToken);
        if (file == null)
        {
            return;
        }

        file.Status = UploadBatchFileStatus.Completed;
        file.CompletedAt = DateTime.UtcNow;
        file.LastError = null;
        file.CurrentTranslationRequestId = request.Id;

        await DeleteArtifactsForFileKindsAsync(
            file.Id,
            [UploadArtifactKind.TranslatedSubtitle, UploadArtifactKind.RemuxedMedia],
            cancellationToken);

        var existingOutputPaths = outputPaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToList();

        foreach (var outputPath in existingOutputPaths)
        {
            EnsurePathWithinBatchStorageRoot(outputPath, file.UploadBatch.StoragePath, "translated output");

            await UpsertArtifactAsync(
                file.UploadBatch,
                file,
                UploadArtifactKind.TranslatedSubtitle,
                outputPath,
                isDownloadable: true,
                cancellationToken: cancellationToken);
        }

        if (file.FileKind == UploadBatchFileKind.Media &&
            file.EmbedTranslatedSubtitle &&
            existingOutputPaths.Count > 0)
        {
            var firstOutputPath = existingOutputPaths[0];
            var remuxedPath = await CreateRemuxedOutputAsync(
                file,
                request.TargetLanguage,
                firstOutputPath,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(remuxedPath) && File.Exists(remuxedPath))
            {
                await UpsertArtifactAsync(
                    file.UploadBatch,
                    file,
                    UploadArtifactKind.RemuxedMedia,
                    remuxedPath,
                    isDownloadable: true,
                    cancellationToken: cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(file.UploadBatchId, cancellationToken);
    }

    public async Task HandleRequestFailedAsync(
        TranslationRequest request,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        if (!request.UploadBatchFileId.HasValue)
        {
            return;
        }

        var file = await _dbContext.UploadBatchFiles.FirstOrDefaultAsync(
            item => item.Id == request.UploadBatchFileId.Value,
            cancellationToken);
        if (file == null)
        {
            return;
        }

        file.Status = UploadBatchFileStatus.Failed;
        file.CompletedAt = DateTime.UtcNow;
        file.LastError = failureMessage;
        file.CurrentTranslationRequestId = request.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(file.UploadBatchId, cancellationToken);
    }

    public async Task HandleRequestCancelledAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.UploadBatchFileId.HasValue)
        {
            return;
        }

        var file = await _dbContext.UploadBatchFiles.FirstOrDefaultAsync(
            item => item.Id == request.UploadBatchFileId.Value,
            cancellationToken);
        if (file == null)
        {
            return;
        }

        file.Status = UploadBatchFileStatus.Cancelled;
        file.CompletedAt = DateTime.UtcNow;
        file.LastError = "Cancelled.";
        file.CurrentTranslationRequestId = request.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(file.UploadBatchId, cancellationToken);
    }

    public async Task<int> CleanupExpiredBatchesAsync(CancellationToken cancellationToken = default)
    {
        var expiredBatchIds = await _dbContext.UploadBatches
            .Where(item => item.ExpiresAt.HasValue && item.ExpiresAt <= DateTime.UtcNow)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        foreach (var batchId in expiredBatchIds)
        {
            await DeleteBatchAsync(batchId, cancellationToken);
        }

        return expiredBatchIds.Count;
    }

    public async Task<int> CleanupExpiredArtifactsAsync(CancellationToken cancellationToken = default)
    {
        var artifacts = await _dbContext.UploadArtifacts
            .Where(item => item.ExpiresAt.HasValue && item.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        if (artifacts.Count == 0)
        {
            return 0;
        }

        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);

        foreach (var artifact in artifacts)
        {
            DeleteFileSafe(artifact.Path, workspaceRoot);

            _dbContext.UploadArtifacts.Remove(artifact);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return artifacts.Count;
    }

    public async Task<int> CleanupStaleIntermediatesAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-6);
        var artifacts = await (
            from artifact in _dbContext.UploadArtifacts
            join file in _dbContext.UploadBatchFiles
                on artifact.UploadBatchFileId equals (int?)file.Id
            where artifact.Kind == UploadArtifactKind.ExtractedSubtitle
            where artifact.CreatedAt <= cutoff
            where file.Status != UploadBatchFileStatus.Processing
            select artifact)
            .ToListAsync(cancellationToken);
        if (artifacts.Count == 0)
        {
            return 0;
        }

        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);

        foreach (var artifact in artifacts)
        {
            DeleteFileSafe(artifact.Path, workspaceRoot);

            _dbContext.UploadArtifacts.Remove(artifact);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return artifacts.Count;
    }

    private async Task ProbeFileInternalAsync(int fileId, CancellationToken cancellationToken)
    {
        var file = await _dbContext.UploadBatchFiles
            .Include(item => item.UploadBatch)
            .Include(item => item.SubtitleStreams)
            .FirstOrDefaultAsync(item => item.Id == fileId, cancellationToken);
        if (file == null)
        {
            return;
        }

        file.ProbeError = null;
        file.LastError = null;
        file.DetectedSourceLanguage = null;
        file.SelectedSourceLanguage = null;
        file.SelectedEmbeddedStreamIndex = null;
        file.SelectedEmbeddedStreamLanguage = null;
        file.SelectedEmbeddedStreamTitle = null;
        file.SelectedEmbeddedStreamCodec = null;
        file.SubtitleStreams.Clear();
        var configuredSourceLanguages = await GetConfiguredSourceLanguagesAsync();

        try
        {
            if (file.FileKind == UploadBatchFileKind.Subtitle)
            {
                file.DetectedSourceLanguage = await DetectSubtitleLanguageAsync(
                    file.StoredPath,
                    configuredSourceLanguages);
                file.DetectedSourceLanguage ??= SubtitleLanguageHelper.DetectLanguageFromFileName(
                    file.OriginalFileName,
                    configuredSourceLanguages);
                file.SelectedSourceLanguage = file.DetectedSourceLanguage;

                if (string.IsNullOrWhiteSpace(file.SelectedSourceLanguage))
                {
                    file.ProbeError = configuredSourceLanguages.Count > 0
                        ? "Could not confidently detect a source language from this subtitle file that matches configured source languages."
                        : "Could not confidently detect a source language from this subtitle file.";
                }
            }
            else
            {
                var streams = await _subtitleExtractionService.ProbeEmbeddedSubtitles(file.StoredPath);
                foreach (var stream in streams)
                {
                    file.SubtitleStreams.Add(new UploadBatchFileSubtitleStream
                    {
                        UploadBatchFileId = file.Id,
                        UploadBatchFile = file,
                        StreamIndex = stream.StreamIndex,
                        Language = NormalizeLanguage(stream.Language),
                        Title = stream.Title,
                        CodecName = stream.CodecName,
                        IsTextBased = stream.IsTextBased,
                        IsDefault = stream.IsDefault,
                        IsForced = stream.IsForced
                    });
                }

                var textBasedStreams = streams
                    .Where(stream => stream.IsTextBased)
                    .ToList();
                EmbeddedSubtitle? bestStream;
                string? matchedLanguage = null;

                if (configuredSourceLanguages.Count > 0)
                {
                    var bestMatch = SubtitleLanguageHelper.FindBestMatch(textBasedStreams, configuredSourceLanguages);
                    bestStream = bestMatch.Subtitle;
                    matchedLanguage = NormalizeLanguage(bestMatch.MatchedLanguage);
                }
                else
                {
                    bestStream = textBasedStreams
                        .OrderByDescending(stream => SubtitleLanguageHelper.ScoreSubtitleCandidate(stream, stream.Language))
                        .ThenBy(stream => stream.StreamIndex)
                        .FirstOrDefault();
                }

                if (bestStream != null)
                {
                    file.DetectedSourceLanguage = !string.IsNullOrWhiteSpace(matchedLanguage)
                        ? matchedLanguage
                        : NormalizeLanguage(bestStream.Language);
                    file.SelectedSourceLanguage = file.DetectedSourceLanguage;
                    file.SelectedEmbeddedStreamIndex = bestStream.StreamIndex;
                    file.SelectedEmbeddedStreamLanguage = NormalizeLanguage(bestStream.Language);
                    file.SelectedEmbeddedStreamTitle = bestStream.Title;
                    file.SelectedEmbeddedStreamCodec = bestStream.CodecName;
                }
                else
                {
                    file.ProbeError = textBasedStreams.Count == 0
                        ? "No text-based subtitle streams were found in this media file."
                        : "No text-based subtitle stream matches the configured source languages.";
                }
            }

            file.ProbeCompletedAt = DateTime.UtcNow;
            UpdateFileStatusForConfiguration(file);
        }
        catch (Exception ex)
        {
            file.Status = UploadBatchFileStatus.Failed;
            file.ProbeCompletedAt = DateTime.UtcNow;
            file.ProbeError = ex.Message;
            file.LastError = ex.Message;
            _logger.LogWarning(ex, "Failed to probe upload file {UploadBatchFileId}", file.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<UploadBatch?> LoadBatchAsync(int batchId, CancellationToken cancellationToken)
    {
        return await _dbContext.UploadBatches
            .Include(batch => batch.Files)
            .ThenInclude(file => file.SubtitleStreams)
            .Include(batch => batch.Files)
            .ThenInclude(file => file.Artifacts)
            .Include(batch => batch.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
    }

    private async Task ValidateIncomingFilesAsync(
        UploadBatch batch,
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var maxBatchSize = await GetMaxBatchSizeAsync(cancellationToken);
        if (batch.Files.Count + files.Count > maxBatchSize)
        {
            throw new InvalidOperationException(
                $"This batch would exceed the configured batch-size limit of {maxBatchSize} files.");
        }

        var maxFileSizeBytes = await GetMaxFileSizeBytesAsync(cancellationToken);
        foreach (var file in files)
        {
            var sanitizedFileName = SanitizeFileName(file.FileName);
            var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
            if (!IsAllowedUploadExtension(extension))
            {
                throw new InvalidOperationException($"Unsupported upload file type: {sanitizedFileName}");
            }

            if (file.Length > maxFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"{sanitizedFileName} exceeds the configured file-size limit of {maxFileSizeBytes} bytes.");
            }
        }

        var incomingBytes = files.Sum(file => file.Length);
        EnsureEnoughDiskSpace(batch.StoragePath, incomingBytes);
    }

    private async Task<UploadBatchFile> IngestStoredFileAsync(
        UploadBatch batch,
        string sanitizedOriginalFileName,
        string storedPath,
        long fileSizeBytes,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sanitizedOriginalFileName).ToLowerInvariant();
        var fullStoredPath = Path.GetFullPath(storedPath);
        EnsurePathWithinBatchStorageRoot(fullStoredPath, batch.StoragePath, "uploaded file");

        var uploadFile = new UploadBatchFile
        {
            UploadBatchId = batch.Id,
            UploadBatch = batch,
            FileKind = IsSubtitleExtension(extension) ? UploadBatchFileKind.Subtitle : UploadBatchFileKind.Media,
            Status = UploadBatchFileStatus.Uploaded,
            Title = Path.GetFileNameWithoutExtension(sanitizedOriginalFileName),
            OriginalFileName = sanitizedOriginalFileName,
            StoredPath = fullStoredPath,
            RelativeStoredPath = Path.GetRelativePath(batch.StoragePath, fullStoredPath),
            FileSizeBytes = fileSizeBytes,
            EmbedTranslatedSubtitle = !IsSubtitleExtension(extension) && batch.DefaultRemuxEnabled
        };

        var originalArtifact = new UploadArtifact
        {
            UploadBatchId = batch.Id,
            UploadBatch = batch,
            UploadBatchFile = uploadFile,
            Kind = UploadArtifactKind.OriginalUpload,
            FileName = Path.GetFileName(fullStoredPath),
            Path = fullStoredPath,
            RelativePath = Path.GetRelativePath(batch.StoragePath, fullStoredPath),
            FileSizeBytes = fileSizeBytes,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? GetContentType(fullStoredPath) : contentType,
            IsDownloadable = true,
            ExpiresAt = batch.ExpiresAt
        };

        _dbContext.UploadBatchFiles.Add(uploadFile);
        _dbContext.UploadArtifacts.Add(originalArtifact);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await ProbeFileInternalAsync(uploadFile.Id, cancellationToken);
        return uploadFile;
    }

    private async Task<string> GetWorkspaceRootAsync(CancellationToken cancellationToken)
    {
        var configured = await _settingService.GetSetting(SettingKeys.UploadWorkspace.StorageRoot);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("Upload workspace storage root is not configured.");
        }

        var fullPath = Path.GetFullPath(configured);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private async Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var raw = await _settingService.GetSetting(SettingKeys.UploadWorkspace.RetentionDays);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 7;
    }

    private async Task<int> GetMaxBatchSizeAsync(CancellationToken cancellationToken)
    {
        var raw = await _settingService.GetSetting(SettingKeys.UploadWorkspace.MaxBatchSize);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 100;
    }

    private async Task<long> GetMaxFileSizeBytesAsync(CancellationToken cancellationToken)
    {
        var raw = await _settingService.GetSetting(SettingKeys.UploadWorkspace.MaxFileSizeBytes);
        return long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 2L * 1024 * 1024 * 1024;
    }

    private static string GetOriginalsDirectory(string batchStoragePath)
    {
        return Path.Combine(batchStoragePath, "originals");
    }

    private static string GetExtractedDirectory(string batchStoragePath)
    {
        return Path.Combine(batchStoragePath, "extracted");
    }

    private static string GetTranslatedDirectory(string batchStoragePath)
    {
        return Path.Combine(batchStoragePath, "translated");
    }

    private static string GetRemuxedDirectory(string batchStoragePath)
    {
        return Path.Combine(batchStoragePath, "remuxed");
    }

    private static string GetIncomingDirectory(string batchStoragePath)
    {
        return Path.Combine(batchStoragePath, "incoming");
    }

    private static string GetIncomingSessionDirectory(string batchStoragePath, Guid uploadId)
    {
        return Path.Combine(GetIncomingDirectory(batchStoragePath), uploadId.ToString("D"));
    }

    private static string GetChunkManifestPath(string sessionDirectory)
    {
        return Path.Combine(sessionDirectory, "manifest.json");
    }

    private static string GetChunkPath(string sessionDirectory, int chunkIndex)
    {
        return Path.Combine(sessionDirectory, $"chunk-{chunkIndex:D6}.part");
    }

    private static void EnsureBatchDirectories(string batchStoragePath)
    {
        Directory.CreateDirectory(batchStoragePath);
        Directory.CreateDirectory(GetOriginalsDirectory(batchStoragePath));
        Directory.CreateDirectory(GetExtractedDirectory(batchStoragePath));
        Directory.CreateDirectory(GetTranslatedDirectory(batchStoragePath));
        Directory.CreateDirectory(GetRemuxedDirectory(batchStoragePath));
        Directory.CreateDirectory(GetIncomingDirectory(batchStoragePath));
    }

    private async Task<string?> DetectSubtitleLanguageAsync(
        string subtitlePath,
        IReadOnlyCollection<string>? configuredLanguages)
    {
        var directory = Path.GetDirectoryName(subtitlePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var matches = await _subtitleService.GetAllSubtitles(directory);
        var configuredSet = configuredLanguages == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(configuredLanguages, StringComparer.OrdinalIgnoreCase);
        var useConfiguredFilter = configuredSet.Count > 0;

        foreach (var subtitle in matches.Where(item => string.Equals(item.Path, subtitlePath, PathComparison)))
        {
            if (!SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(subtitle.Language, out var normalizedLanguage))
            {
                continue;
            }

            if (useConfiguredFilter && !configuredSet.Contains(normalizedLanguage))
            {
                continue;
            }

            return normalizedLanguage;
        }

        return null;
    }

    private async Task<List<string>> GetConfiguredSourceLanguagesAsync()
    {
        try
        {
            var sourceLanguages = await _settingService.GetSettingAsJson<Lingarr.Server.Models.SourceLanguage>(
                SettingKeys.Translation.SourceLanguages);
            return sourceLanguages
                .Select(language => language.Code)
                .Select(code => SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(code, out var normalizedCode)
                    ? normalizedCode
                    : null)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private async Task EnsureBatchHasCapacityAsync(
        int currentFileCount,
        int additionalFiles,
        CancellationToken cancellationToken)
    {
        var maxBatchSize = await GetMaxBatchSizeAsync(cancellationToken);
        if (currentFileCount + additionalFiles > maxBatchSize)
        {
            throw new InvalidOperationException(
                $"This batch would exceed the configured batch-size limit of {maxBatchSize} files.");
        }
    }

    private void UpdateFileStatusForConfiguration(UploadBatchFile file, string? targetLanguageOverride = null)
    {
        if (file.ExcludeFromTranslation)
        {
            file.Status = UploadBatchFileStatus.Cancelled;
            return;
        }

        if (string.Equals(file.ProbeError, SourceLanguageMatchesTargetMessage, StringComparison.Ordinal))
        {
            file.ProbeError = null;
        }

        if (string.Equals(file.LastError, SourceLanguageMatchesTargetMessage, StringComparison.Ordinal))
        {
            file.LastError = null;
        }

        if (IsSourceLanguageMatchingTarget(file, targetLanguageOverride))
        {
            file.Status = UploadBatchFileStatus.NeedsConfiguration;

            if (file.FileKind == UploadBatchFileKind.Subtitle)
            {
                file.ProbeError = SourceLanguageMatchesTargetMessage;
            }
            else
            {
                file.LastError = SourceLanguageMatchesTargetMessage;
            }

            return;
        }

        if (file.FileKind == UploadBatchFileKind.Subtitle)
        {
            file.Status = string.IsNullOrWhiteSpace(file.SelectedSourceLanguage)
                ? UploadBatchFileStatus.NeedsConfiguration
                : UploadBatchFileStatus.Ready;
            return;
        }

        if (!file.SelectedEmbeddedStreamIndex.HasValue || string.IsNullOrWhiteSpace(file.SelectedSourceLanguage))
        {
            file.Status = string.IsNullOrWhiteSpace(file.ProbeError)
                ? UploadBatchFileStatus.NeedsConfiguration
                : UploadBatchFileStatus.Failed;
            return;
        }

        file.Status = UploadBatchFileStatus.Ready;
    }

    private static bool IsSourceLanguageMatchingTarget(UploadBatchFile file, string? targetLanguageOverride = null)
    {
        var sourceLanguage = NormalizeLanguage(file.SelectedSourceLanguage ?? file.DetectedSourceLanguage);
        var targetLanguage = NormalizeLanguage(targetLanguageOverride ?? file.UploadBatch?.TargetLanguage);

        return !string.IsNullOrWhiteSpace(sourceLanguage) &&
               !string.IsNullOrWhiteSpace(targetLanguage) &&
               string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanQueueFile(UploadBatchFile file)
    {
        return file.Status is UploadBatchFileStatus.Ready or UploadBatchFileStatus.Completed or UploadBatchFileStatus.Failed or UploadBatchFileStatus.Cancelled;
    }

    private static void ValidateQueuePrerequisites(UploadBatchFile file)
    {
        _ = GetSourceLanguageForQueue(file);

        if (file.FileKind == UploadBatchFileKind.Media &&
            (!file.SelectedEmbeddedStreamIndex.HasValue || string.IsNullOrWhiteSpace(file.SelectedEmbeddedStreamCodec)))
        {
            throw new InvalidOperationException(
                $"No text-based subtitle stream is selected for media file {file.OriginalFileName}.");
        }
    }

    private static string GetSourceLanguageForQueue(UploadBatchFile file)
    {
        return file.SelectedSourceLanguage
            ?? file.DetectedSourceLanguage
            ?? throw new InvalidOperationException($"No source language configured for {file.OriginalFileName}.");
    }

    private async Task RefreshBatchStatusAsync(int batchId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return;
        }

        var actionableFiles = batch.Files.Where(file => !file.ExcludeFromTranslation).ToList();
        if (actionableFiles.Count == 0)
        {
            batch.Status = UploadBatchStatus.Draft;
            batch.CompletedAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (actionableFiles.Any(file => file.Status is UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing))
        {
            batch.Status = UploadBatchStatus.Processing;
            batch.CompletedAt = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (actionableFiles.All(file => file.Status == UploadBatchFileStatus.Completed))
        {
            batch.Status = UploadBatchStatus.Completed;
            batch.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (actionableFiles.All(file =>
                file.Status is UploadBatchFileStatus.Completed or UploadBatchFileStatus.Cancelled or UploadBatchFileStatus.Failed))
        {
            batch.Status = actionableFiles.Any(file => file.Status == UploadBatchFileStatus.Failed)
                ? UploadBatchStatus.Failed
                : UploadBatchStatus.Cancelled;
            batch.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        batch.Status = UploadBatchStatus.Ready;
        batch.CompletedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeLanguage(string? language)
    {
        return string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var normalizedFileName = PathStringHelper.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "upload";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            normalizedFileName = normalizedFileName.Replace(invalidChar, '_');
        }

        normalizedFileName = normalizedFileName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "upload";
        }

        var extension = Path.GetExtension(normalizedFileName);
        var baseName = Path.GetFileNameWithoutExtension(normalizedFileName)
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "upload";
        }

        return string.IsNullOrWhiteSpace(extension) ? baseName : $"{baseName}{extension}";
    }

    private static ReservedUploadFile ReserveUniqueFile(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var index = 1;

        while (true)
        {
            var fullPath = Path.Combine(directory, candidate);

            try
            {
                var stream = new FileStream(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    options: FileOptions.Asynchronous);
                return new ReservedUploadFile(candidate, fullPath, stream);
            }
            catch (IOException) when (File.Exists(fullPath))
            {
                candidate = $"{baseName}_{index}{extension}";
                index++;
            }
        }
    }

    private static async Task<CachedLockLease<TKey>> AcquireCachedLockAsync<TKey>(
        ConcurrentDictionary<TKey, RefCountedSemaphore> locks,
        TKey key,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        var cachedLock = RentCachedLock(locks, key);
        var lockTaken = false;
        try
        {
            await cachedLock.Semaphore.WaitAsync(cancellationToken);
            lockTaken = true;
            return new CachedLockLease<TKey>(locks, key, cachedLock);
        }
        catch
        {
            ReleaseCachedLock(locks, key, cachedLock, lockTaken);
            throw;
        }
    }

    private static RefCountedSemaphore RentCachedLock<TKey>(
        ConcurrentDictionary<TKey, RefCountedSemaphore> locks,
        TKey key)
        where TKey : notnull
    {
        lock (CachedLockGate)
        {
            if (!locks.TryGetValue(key, out var cachedLock))
            {
                cachedLock = new RefCountedSemaphore();
                locks[key] = cachedLock;
            }

            cachedLock.ReferenceCount++;
            return cachedLock;
        }
    }

    private static void ReleaseCachedLock<TKey>(
        ConcurrentDictionary<TKey, RefCountedSemaphore> locks,
        TKey key,
        RefCountedSemaphore cachedLock,
        bool releaseSemaphore)
        where TKey : notnull
    {
        if (releaseSemaphore)
        {
            cachedLock.Semaphore.Release();
        }

        lock (CachedLockGate)
        {
            cachedLock.ReferenceCount--;
            if (cachedLock.ReferenceCount == 0)
            {
                locks.TryRemove(new KeyValuePair<TKey, RefCountedSemaphore>(key, cachedLock));
            }
        }
    }

    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class CachedLockLease<TKey> : IDisposable
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, RefCountedSemaphore> _locks;
        private readonly TKey _key;
        private readonly RefCountedSemaphore _cachedLock;
        private bool _disposed;

        public CachedLockLease(
            ConcurrentDictionary<TKey, RefCountedSemaphore> locks,
            TKey key,
            RefCountedSemaphore cachedLock)
        {
            _locks = locks;
            _key = key;
            _cachedLock = cachedLock;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ReleaseCachedLock(_locks, _key, _cachedLock, releaseSemaphore: true);
            _disposed = true;
        }
    }

    private sealed record ReservedUploadFile(string FileName, string FullPath, FileStream Stream);
    private sealed record UploadChunkSessionContext(
        UploadBatch Batch,
        UploadChunkManifest Manifest,
        string SessionDirectory,
        string ManifestPath);

    private sealed class UploadChunkManifest
    {
        public Guid UploadId { get; set; }
        public int BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string? ContentType { get; set; }
        public DateTime? LastModifiedUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public Dictionary<int, long> ChunkSizes { get; set; } = [];
    }

    private static bool IsSubtitleExtension(string extension)
    {
        return SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsAllowedUploadExtension(string extension)
    {
        return IsSubtitleExtension(extension) || MediaExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureEnoughDiskSpace(string batchStoragePath, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(batchStoragePath));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var driveInfo = new DriveInfo(root);
            if (driveInfo.AvailableFreeSpace < requiredBytes)
            {
                throw new InvalidOperationException("Not enough free disk space for the requested upload.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private async Task DeleteGeneratedArtifactsForFileAsync(UploadBatchFile file, CancellationToken cancellationToken)
    {
        await DeleteArtifactsForFileKindsAsync(
            file.Id,
            [UploadArtifactKind.TranslatedSubtitle, UploadArtifactKind.RemuxedMedia],
            cancellationToken);
    }

    private async Task DeleteArtifactsForFileKindsAsync(
        int uploadBatchFileId,
        UploadArtifactKind[] kinds,
        CancellationToken cancellationToken)
    {
        var artifacts = await _dbContext.UploadArtifacts
            .Where(item => item.UploadBatchFileId == uploadBatchFileId && kinds.Contains(item.Kind))
            .ToListAsync(cancellationToken);
        if (artifacts.Count == 0)
        {
            return;
        }

        var workspaceRoot = await GetWorkspaceRootAsync(cancellationToken);

        foreach (var artifact in artifacts)
        {
            DeleteFileSafe(artifact.Path, workspaceRoot);

            _dbContext.UploadArtifacts.Remove(artifact);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertArtifactAsync(
        UploadBatch batch,
        UploadBatchFile file,
        UploadArtifactKind kind,
        string artifactPath,
        bool isDownloadable,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(artifactPath);
        EnsurePathWithinBatchStorageRoot(fullPath, batch.StoragePath, "artifact");
        var fileInfo = new FileInfo(fullPath);
        var relativePath = Path.GetRelativePath(batch.StoragePath, fullPath);

        var existing = await _dbContext.UploadArtifacts.FirstOrDefaultAsync(item =>
                item.UploadBatchFileId == file.Id &&
                item.Kind == kind &&
                item.Path == fullPath,
            cancellationToken);

        if (existing == null)
        {
            _dbContext.UploadArtifacts.Add(new UploadArtifact
            {
                UploadBatchId = batch.Id,
                UploadBatch = batch,
                UploadBatchFileId = file.Id,
                UploadBatchFile = file,
                Kind = kind,
                FileName = Path.GetFileName(fullPath),
                Path = fullPath,
                RelativePath = relativePath,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                ContentType = GetContentType(fullPath),
                IsDownloadable = isDownloadable,
                ExpiresAt = batch.ExpiresAt
            });
            return;
        }

        existing.FileName = Path.GetFileName(fullPath);
        existing.RelativePath = relativePath;
        existing.FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
        existing.ContentType = GetContentType(fullPath);
        existing.IsDownloadable = isDownloadable;
        existing.ExpiresAt = batch.ExpiresAt;
    }

    private async Task<string?> CreateRemuxedOutputAsync(
        UploadBatchFile file,
        string targetLanguage,
        string translatedSubtitlePath,
        CancellationToken cancellationToken)
    {
        EnsurePathWithinBatchStorageRoot(translatedSubtitlePath, file.UploadBatch.StoragePath, "translated subtitle");

        var remuxDirectory = GetRemuxedDirectory(file.UploadBatch.StoragePath);
        Directory.CreateDirectory(remuxDirectory);

        var sanitizedOriginalFileName = SanitizeFileName(file.OriginalFileName);
        var extension = Path.GetExtension(sanitizedOriginalFileName);
        var baseName = Path.GetFileNameWithoutExtension(sanitizedOriginalFileName);
        var outputPath = Path.Combine(remuxDirectory, $"{baseName}.{targetLanguage}.lingarr{extension}");
        EnsurePathWithinBatchStorageRoot(outputPath, file.UploadBatch.StoragePath, "remuxed output");

        var existingSubtitleCount = (await _subtitleExtractionService.ProbeEmbeddedSubtitles(file.StoredPath)).Count;
        var subtitleCodec = extension.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "mov_text",
            _ => "srt"
        };

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(file.StoredPath);
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(translatedSubtitlePath);
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("1:0");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add("-c:s");
        process.StartInfo.ArgumentList.Add(subtitleCodec);
        process.StartInfo.ArgumentList.Add($"-metadata:s:s:{existingSubtitleCount}");
        process.StartInfo.ArgumentList.Add($"language={NormalizeLanguage(targetLanguage) ?? targetLanguage}");
        process.StartInfo.ArgumentList.Add($"-metadata:s:s:{existingSubtitleCount}");
        process.StartInfo.ArgumentList.Add($"title=Lingarr {targetLanguage}");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogWarning(
                "Failed to remux upload file {UploadBatchFileId}. Exit={ExitCode}. Error={Error}",
                file.Id,
                process.ExitCode,
                stderr);
            return null;
        }

        return outputPath;
    }

    private async Task<UploadChunkSessionContext?> LoadChunkSessionContextAsync(
        int batchId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.UploadBatches
            .Include(item => item.Files)
            .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
        if (batch == null)
        {
            return null;
        }

        var sessionDirectory = GetIncomingSessionDirectory(batch.StoragePath, uploadId);
        if (!Directory.Exists(sessionDirectory))
        {
            return null;
        }

        EnsurePathWithinBatchStorageRoot(sessionDirectory, batch.StoragePath, "chunk upload session");
        var manifestPath = GetChunkManifestPath(sessionDirectory);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = await ReadChunkManifestAsync(manifestPath, cancellationToken);
        if (manifest == null)
        {
            throw new InvalidOperationException("Chunk upload session manifest is missing or invalid.");
        }

        if (manifest.BatchId != batchId || manifest.UploadId != uploadId)
        {
            throw new InvalidOperationException("Chunk upload session does not belong to this batch.");
        }

        return new UploadChunkSessionContext(batch, manifest, sessionDirectory, manifestPath);
    }

    private static async Task<UploadChunkManifest?> ReadChunkManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var manifestStream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkFileBufferSize,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<UploadChunkManifest>(
            manifestStream,
            ManifestSerializerOptions,
            cancellationToken);
    }

    private static async Task WriteChunkManifestAsync(
        string manifestPath,
        UploadChunkManifest manifest,
        CancellationToken cancellationToken)
    {
        var temporaryManifestPath = $"{manifestPath}.tmp";

        await using (var manifestStream = new FileStream(
                         temporaryManifestPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         ChunkFileBufferSize,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                manifest,
                ManifestSerializerOptions,
                cancellationToken);
            await manifestStream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryManifestPath, manifestPath, overwrite: true);
    }

    private static UploadChunkSessionResponse ToChunkSessionResponse(UploadChunkManifest manifest)
    {
        var chunkSizeBytes = Math.Min(DefaultChunkSizeBytes, MaxChunkSizeBytes);
        return new UploadChunkSessionResponse
        {
            UploadId = manifest.UploadId,
            FileName = manifest.FileName,
            FileSizeBytes = manifest.FileSizeBytes,
            ContentType = manifest.ContentType,
            LastModifiedUtc = manifest.LastModifiedUtc,
            ChunkSizeBytes = chunkSizeBytes,
            MaxChunkSizeBytes = MaxChunkSizeBytes,
            ExpectedChunks = Math.Max(1, (int)Math.Ceiling((double)manifest.FileSizeBytes / chunkSizeBytes)),
            CreatedAtUtc = manifest.CreatedAtUtc,
            UpdatedAtUtc = manifest.UpdatedAtUtc,
            UploadedChunkCount = manifest.ChunkSizes.Count,
            UploadedBytes = manifest.ChunkSizes.Values.Sum()
        };
    }

    private static async Task<long> CopyStreamWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkFileBufferSize];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Chunk size exceeds the maximum allowed size of {maxBytes} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return totalBytes;
    }

    private static void EnsurePathWithinBatchStorageRoot(string path, string batchStoragePath, string pathKind)
    {
        if (IsPathWithinWorkspaceRoot(path, batchStoragePath))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to use {pathKind} path outside upload batch storage.");
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".srt" => "application/x-subrip",
            ".ass" => "text/x-ssa",
            ".ssa" => "text/x-ssa",
            ".vtt" => "text/vtt",
            ".mp4" => "video/mp4",
            ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }

    private static void DeleteFileSafe(string filePath, string workspaceRoot)
    {
        if (!IsPathWithinWorkspaceRoot(filePath, workspaceRoot))
        {
            throw new InvalidOperationException("Refusing to delete a file outside the upload workspace root.");
        }

        var fullFilePath = Path.GetFullPath(filePath);
        if (File.Exists(fullFilePath))
        {
            File.Delete(fullFilePath);
        }
    }

    private static void DeleteDirectorySafe(string directoryPath, string workspaceRoot)
    {
        if (!IsPathWithinWorkspaceRoot(directoryPath, workspaceRoot))
        {
            throw new InvalidOperationException("Refusing to delete a path outside the upload workspace root.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        if (string.Equals(fullRoot, fullTarget, PathComparison))
        {
            throw new InvalidOperationException("Refusing to delete the upload workspace root directory.");
        }

        if (Directory.Exists(fullTarget))
        {
            Directory.Delete(fullTarget, recursive: true);
        }
    }

    private static bool IsPathWithinWorkspaceRoot(string path, string workspaceRoot)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = fullRoot + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, fullRoot, PathComparison) ||
               fullPath.StartsWith(rootPrefix, PathComparison);
    }
}
