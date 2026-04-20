using System.Diagnostics;
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
        var batch = await _dbContext.UploadBatches.FirstOrDefaultAsync(
            item => item.Id == batchId,
            cancellationToken);
        if (batch == null)
        {
            return null;
        }

        batch.Name = request.Name.Trim();
        batch.TargetLanguage = NormalizeLanguage(request.TargetLanguage)
            ?? throw new InvalidOperationException("Target language is required.");
        batch.DefaultRemuxEnabled = request.DefaultRemuxEnabled;

        foreach (var file in await _dbContext.UploadBatchFiles
                     .Where(item => item.UploadBatchId == batchId && item.FileKind == UploadBatchFileKind.Media)
                     .ToListAsync(cancellationToken))
        {
            if (file.Status is UploadBatchFileStatus.Uploaded or UploadBatchFileStatus.NeedsConfiguration or UploadBatchFileStatus.Ready)
            {
                file.EmbedTranslatedSubtitle = request.DefaultRemuxEnabled;
            }
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
        var newFiles = new List<UploadBatchFile>();

        foreach (var formFile in files)
        {
            var sanitizedOriginalFileName = SanitizeFileName(formFile.FileName);
            var extension = Path.GetExtension(sanitizedOriginalFileName).ToLowerInvariant();
            var reservedFile = ReserveUniqueFile(originalsDirectory, sanitizedOriginalFileName);
            var safeName = reservedFile.FileName;
            var destinationPath = reservedFile.FullPath;

            await using (reservedFile.Stream)
            {
                await formFile.CopyToAsync(reservedFile.Stream, cancellationToken);
            }

            var uploadFile = new UploadBatchFile
            {
                UploadBatchId = batch.Id,
                UploadBatch = batch,
                FileKind = IsSubtitleExtension(extension) ? UploadBatchFileKind.Subtitle : UploadBatchFileKind.Media,
                Status = UploadBatchFileStatus.Uploaded,
                Title = Path.GetFileNameWithoutExtension(sanitizedOriginalFileName),
                OriginalFileName = sanitizedOriginalFileName,
                StoredPath = destinationPath,
                RelativeStoredPath = Path.GetRelativePath(batch.StoragePath, destinationPath),
                FileSizeBytes = formFile.Length,
                EmbedTranslatedSubtitle = !IsSubtitleExtension(extension) && batch.DefaultRemuxEnabled
            };

            var originalArtifact = new UploadArtifact
            {
                UploadBatchId = batch.Id,
                UploadBatch = batch,
                UploadBatchFile = uploadFile,
                Kind = UploadArtifactKind.OriginalUpload,
                FileName = safeName,
                Path = destinationPath,
                RelativePath = Path.GetRelativePath(batch.StoragePath, destinationPath),
                FileSizeBytes = formFile.Length,
                ContentType = formFile.ContentType,
                IsDownloadable = true,
                ExpiresAt = batch.ExpiresAt
            };

            _dbContext.UploadBatchFiles.Add(uploadFile);
            _dbContext.UploadArtifacts.Add(originalArtifact);
            newFiles.Add(uploadFile);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var file in newFiles)
        {
            await ProbeFileInternalAsync(file.Id, cancellationToken);
        }

        await RefreshBatchStatusAsync(batch.Id, cancellationToken);
        return await LoadBatchAsync(batch.Id, cancellationToken);
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
        file.SelectedSourceLanguage = NormalizeLanguage(request.SelectedSourceLanguage);

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

        var transaction = _dbContext.Database.IsRelational()
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

            await RefreshBatchStatusAsync(batchId, cancellationToken);
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
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
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
                     item.CurrentTranslationRequestId.HasValue &&
                     item.Status is UploadBatchFileStatus.Queued or UploadBatchFileStatus.Processing))
        {
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
        var artifacts = await _dbContext.UploadArtifacts
            .Where(item => item.Kind == UploadArtifactKind.ExtractedSubtitle)
            .Where(item => item.CreatedAt <= cutoff)
            .Where(item => item.UploadBatchFileId.HasValue)
            .Where(item => _dbContext.UploadBatchFiles
                .Where(file => file.Id == item.UploadBatchFileId.Value)
                .Select(file => file.Status)
                .FirstOrDefault() != UploadBatchFileStatus.Processing)
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

        try
        {
            if (file.FileKind == UploadBatchFileKind.Subtitle)
            {
                file.DetectedSourceLanguage = await DetectSubtitleLanguageAsync(file.StoredPath);
                file.SelectedSourceLanguage = file.DetectedSourceLanguage;
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

                var bestStream = streams
                    .Where(stream => stream.IsTextBased)
                    .OrderByDescending(stream => SubtitleLanguageHelper.ScoreSubtitleCandidate(stream, stream.Language))
                    .ThenBy(stream => stream.StreamIndex)
                    .FirstOrDefault();

                if (bestStream != null)
                {
                    file.DetectedSourceLanguage = NormalizeLanguage(bestStream.Language);
                    file.SelectedSourceLanguage = file.DetectedSourceLanguage;
                    file.SelectedEmbeddedStreamIndex = bestStream.StreamIndex;
                    file.SelectedEmbeddedStreamLanguage = NormalizeLanguage(bestStream.Language);
                    file.SelectedEmbeddedStreamTitle = bestStream.Title;
                    file.SelectedEmbeddedStreamCodec = bestStream.CodecName;
                }
                else
                {
                    file.ProbeError = "No text-based subtitle streams were found in this media file.";
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

    private static void EnsureBatchDirectories(string batchStoragePath)
    {
        Directory.CreateDirectory(batchStoragePath);
        Directory.CreateDirectory(GetOriginalsDirectory(batchStoragePath));
        Directory.CreateDirectory(GetExtractedDirectory(batchStoragePath));
        Directory.CreateDirectory(GetTranslatedDirectory(batchStoragePath));
        Directory.CreateDirectory(GetRemuxedDirectory(batchStoragePath));
    }

    private async Task<string?> DetectSubtitleLanguageAsync(string subtitlePath)
    {
        var directory = Path.GetDirectoryName(subtitlePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var matches = await _subtitleService.GetAllSubtitles(directory);
        return matches
            .Where(item => string.Equals(item.Path, subtitlePath, PathComparison))
            .Select(item => NormalizeLanguage(item.Language))
            .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language));
    }

    private void UpdateFileStatusForConfiguration(UploadBatchFile file)
    {
        if (file.ExcludeFromTranslation)
        {
            file.Status = UploadBatchFileStatus.Cancelled;
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
        var normalizedFileName = Path.GetFileName(fileName);
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

    private sealed record ReservedUploadFile(string FileName, string FullPath, FileStream Stream);

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
