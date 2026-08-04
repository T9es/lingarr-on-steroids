using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lingarr.Server.Services.Translation;

public class TranslationCheckpointService : ITranslationCheckpointService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _checkpointLocks = new(StringComparer.OrdinalIgnoreCase);

    internal static string GetFallbackCheckpointFingerprint(TranslationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceSnapshotFingerprint) &&
            !HasSourceSnapshotSelectionMetadata(request))
        {
            return request.SourceSnapshotFingerprint;
        }

        return HasSourceSnapshotMetadata(request)
            ? BuildSnapshotIdentity(request)
            : BuildLegacyCheckpointFingerprint(request);
    }

    internal static string BuildCheckpointFingerprint(
        TranslationRequest request,
        string contentHash)
    {
        var identity = HasSourceSnapshotMetadata(request)
            ? BuildSnapshotIdentity(request)
            : GetFallbackCheckpointFingerprint(request);
        return $"{identity}|content-sha256:{contentHash}";
    }

    private static bool HasSourceSnapshotMetadata(TranslationRequest request)
    {
        return HasSourceSnapshotSelectionMetadata(request) ||
               !string.IsNullOrWhiteSpace(request.SourceSnapshotFingerprint);
    }

    private static bool HasSourceSnapshotSelectionMetadata(TranslationRequest request)
    {
        return request.SourceSnapshotVersion != 1 ||
               !string.IsNullOrWhiteSpace(request.SourceSnapshotType) ||
               !string.IsNullOrWhiteSpace(request.SourceSnapshotIdentity) ||
               request.SourceSnapshotFileSizeBytes.HasValue ||
               request.SourceSnapshotLastWriteUtc.HasValue ||
               request.SourceSnapshotStreamIndex.HasValue;
    }

    private static string BuildSnapshotIdentity(TranslationRequest request)
    {
        return string.Join(
            "|",
            "checkpoint-source-v2",
            FormatField("path", request.SubtitleToTranslate),
            FormatField("source-language", request.SourceLanguage),
            FormatField("target-language", request.TargetLanguage),
            FormatField("source-format", request.SourceSubtitleFormat),
            FormatField("source-subtitle-type", request.SourceSubtitleType),
            FormatField("selected-stream-title", request.SelectedStreamTitle),
            FormatField("forced", request.IsForcedSubtitle ? "true" : "false"),
            FormatField("snapshot-version", request.SourceSnapshotVersion.ToString(CultureInfo.InvariantCulture)),
            FormatField("snapshot-type", request.SourceSnapshotType),
            FormatField("snapshot-identity", request.SourceSnapshotIdentity),
            FormatField("snapshot-fingerprint", request.SourceSnapshotFingerprint),
            FormatField(
                "snapshot-file-size",
                request.SourceSnapshotFileSizeBytes?.ToString(CultureInfo.InvariantCulture)),
            FormatField(
                "snapshot-last-write-utc-ticks",
                request.SourceSnapshotLastWriteUtc?.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)),
            FormatField(
                "snapshot-stream-index",
                request.SourceSnapshotStreamIndex?.ToString(CultureInfo.InvariantCulture)));
    }

    private static string BuildLegacyCheckpointFingerprint(TranslationRequest request)
    {
        return string.Join(
            "|",
            request.SubtitleToTranslate ?? string.Empty,
            request.SourceLanguage,
            request.TargetLanguage,
            request.SourceSubtitleFormat ?? string.Empty);
    }

    private static string FormatField(string name, string? value)
    {
        var normalizedValue = value ?? string.Empty;
        return $"{name}:{normalizedValue.Length}:{normalizedValue}";
    }

    private sealed class LoadedCheckpointSnapshot
    {
        public string SourceFingerprint { get; init; } = string.Empty;
        public Dictionary<int, string> Translations { get; init; } = new();
        public HashSet<int> SourcePreservedPositions { get; init; } = [];
    }

    private readonly ILogger<TranslationCheckpointService> _logger;
    private readonly string _checkpointRoot;
    private readonly Func<Task>? _beforeCheckpointWriteAsync;
    private readonly LingarrDbContext? _dbContext;
    private readonly ConditionalWeakTable<TranslationCheckpoint, LoadedCheckpointSnapshot> _loadedCheckpointSnapshots = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        LingarrDbContext dbContext)
        : this(
            logger,
            Path.Combine(AppContext.BaseDirectory, "config", "translation-checkpoints"),
            beforeCheckpointWriteAsync: null,
            dbContext: dbContext)
    {
    }

    internal TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        string checkpointRoot)
        : this(logger, checkpointRoot, null, null)
    {
    }

    internal TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        string checkpointRoot,
        Func<Task>? beforeCheckpointWriteAsync)
        : this(logger, checkpointRoot, beforeCheckpointWriteAsync, null)
    {
    }

    internal TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        string checkpointRoot,
        Func<Task>? beforeCheckpointWriteAsync,
        LingarrDbContext? dbContext)
    {
        _logger = logger;
        _checkpointRoot = checkpointRoot;
        _beforeCheckpointWriteAsync = beforeCheckpointWriteAsync;
        _dbContext = dbContext;
    }

    public async Task<TranslationCheckpoint?> LoadAsync(
        int translationRequestId,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpoint = await ReadCheckpointAsync(
            path,
            translationRequestId,
            sourceFingerprint,
            cancellationToken);
        TrackLoadedCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <inheritdoc />
    public async Task<TranslationCheckpoint?> LoadByRequestIdAsync(
        int translationRequestId,
        CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpoint = await ReadCheckpointAsync(
            path,
            translationRequestId,
            sourceFingerprint: null,
            cancellationToken: cancellationToken);
        TrackLoadedCheckpoint(checkpoint);
        return checkpoint;
    }

    /// <inheritdoc />
    public Task SaveCheckpointAsync(
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => SaveCheckpointAsync(checkpoint, cancellationToken, ownershipToken: null);

    public async Task SaveCheckpointAsync(
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken,
        string? ownershipToken)
    {
        Directory.CreateDirectory(_checkpointRoot);
        var path = GetCheckpointPath(checkpoint.TranslationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
            if (_dbContext == null)
            {
                await SaveCheckpointCoreAsync(checkpoint, path, ownershipToken, cancellationToken);
            }
            else
            {
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                    await SaveCheckpointCoreAsync(checkpoint, path, ownershipToken, cancellationToken));
            }
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task SaveCheckpointCoreAsync(
        TranslationCheckpoint checkpoint,
        string path,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        CheckpointOwnershipLease? ownershipLease = null;
        try
        {
            ownershipLease = await BeginAttemptOwnershipLeaseAsync(
                checkpoint.TranslationRequestId,
                ownershipToken,
                cancellationToken);
            var currentCheckpoint = await ReadCheckpointAsync(
                path,
                checkpoint.TranslationRequestId,
                checkpoint.SourceFingerprint,
                cancellationToken);
            var mergedCheckpoint = MergeFullCheckpoint(checkpoint, currentCheckpoint);

            if (_beforeCheckpointWriteAsync != null)
            {
                await _beforeCheckpointWriteAsync();
            }

            await EnsureAttemptOwnershipAsync(
                checkpoint.TranslationRequestId,
                ownershipToken,
                cancellationToken);
            mergedCheckpoint.UpdatedAtUtc = DateTime.UtcNow;
            mergedCheckpoint.OwnershipToken = ownershipToken;
            await WriteCheckpointAtomicallyAsync(
                path,
                mergedCheckpoint,
                cancellationToken,
                () => EnsureAttemptOwnershipAsync(
                    checkpoint.TranslationRequestId,
                    ownershipToken,
                    cancellationToken));

            if (ownershipLease != null)
            {
                await ownershipLease.CommitAsync();
            }

            checkpoint.Translations.Clear();
            foreach (var translation in mergedCheckpoint.Translations)
            {
                checkpoint.Translations[translation.Key] = translation.Value;
            }

            checkpoint.SourcePreservedPositions.Clear();
            foreach (var position in mergedCheckpoint.SourcePreservedPositions)
            {
                checkpoint.SourcePreservedPositions.Add(position);
            }

            checkpoint.UpdatedAtUtc = mergedCheckpoint.UpdatedAtUtc;
            checkpoint.OwnershipToken = mergedCheckpoint.OwnershipToken;
            TrackLoadedCheckpoint(checkpoint);
        }
        finally
        {
            if (ownershipLease != null)
            {
                await ownershipLease.DisposeAsync();
            }
        }
    }

    public Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken)
        => SaveTranslationAsync(
            translationRequestId,
            sourceFingerprint,
            position,
            translatedText,
            cancellationToken,
            ownershipToken: null);

    public async Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken,
        string? ownershipToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
            if (_dbContext == null)
            {
                await SaveTranslationCoreAsync(
                    path, translationRequestId, sourceFingerprint, position, translatedText, ownershipToken, cancellationToken);
            }
            else
            {
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                    await SaveTranslationCoreAsync(
                        path, translationRequestId, sourceFingerprint, position, translatedText, ownershipToken, cancellationToken));
            }
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task SaveTranslationCoreAsync(
        string path,
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        CheckpointOwnershipLease? ownershipLease = null;
        try
        {
            ownershipLease = await BeginAttemptOwnershipLeaseAsync(
                translationRequestId,
                ownershipToken,
                cancellationToken);
            Directory.CreateDirectory(_checkpointRoot);
            var checkpoint = await LoadAsync(translationRequestId, sourceFingerprint, cancellationToken) ??
                             new TranslationCheckpoint
                             {
                                 TranslationRequestId = translationRequestId,
                                 SourceFingerprint = sourceFingerprint
                             };

            if (_beforeCheckpointWriteAsync != null)
            {
                await _beforeCheckpointWriteAsync();
            }

            await EnsureAttemptOwnershipAsync(
                translationRequestId,
                ownershipToken,
                cancellationToken);
            checkpoint.Translations[position] = translatedText;
            checkpoint.UpdatedAtUtc = DateTime.UtcNow;
            checkpoint.OwnershipToken = ownershipToken;
            await WriteCheckpointAtomicallyAsync(
                path,
                checkpoint,
                cancellationToken,
                () => EnsureAttemptOwnershipAsync(
                    translationRequestId,
                    ownershipToken,
                    cancellationToken));
            if (ownershipLease != null)
            {
                await ownershipLease.CommitAsync();
            }

            TrackLoadedCheckpoint(checkpoint);
        }
        finally
        {
            if (ownershipLease != null)
            {
                await ownershipLease.DisposeAsync();
            }
        }
    }

    public Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
        => DeleteAsync(translationRequestId, cancellationToken, ownershipToken: null);

    public async Task DeleteAsync(
        int translationRequestId,
        CancellationToken cancellationToken,
        string? ownershipToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
            if (_dbContext == null)
            {
                await DeleteCheckpointCoreAsync(path, translationRequestId, ownershipToken, cancellationToken);
            }
            else
            {
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                    await DeleteCheckpointCoreAsync(path, translationRequestId, ownershipToken, cancellationToken));
            }
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task DeleteCheckpointCoreAsync(
        string path,
        int translationRequestId,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        CheckpointDatabaseLease? deletionLease = null;
        try
        {
            deletionLease = await BeginCheckpointDeleteLeaseAsync(
                translationRequestId,
                path,
                ownershipToken,
                cancellationToken);
            if (deletionLease != null && !deletionLease.CanProceed)
            {
                return;
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete translation checkpoint for request {RequestId}", translationRequestId);
                }
            }

            if (deletionLease != null)
            {
                await deletionLease.CommitAsync();
            }
        }
        finally
        {
            if (deletionLease != null)
            {
                await deletionLease.DisposeAsync();
            }
        }
    }

    private async Task<CheckpointOwnershipLease?> BeginAttemptOwnershipLeaseAsync(
        int translationRequestId,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null || string.IsNullOrWhiteSpace(ownershipToken))
        {
            return null;
        }

        if (_dbContext.Database.CurrentTransaction != null)
        {
            await EnsureAttemptOwnershipAsync(
                translationRequestId,
                ownershipToken,
                cancellationToken);
            return null;
        }

        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var rowsUpdated = await _dbContext.TranslationRequests
                .Where(request => request.Id == translationRequestId &&
                                  request.Status == TranslationStatus.InProgress &&
                                  request.JobId == ownershipToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(request => request.UpdatedAt, DateTime.UtcNow), cancellationToken);

            if (rowsUpdated == 0)
            {
                throw CreateOwnershipLostException(translationRequestId, ownershipToken, cancellationToken);
            }

            return new CheckpointOwnershipLease(transaction);
        }
        catch
        {
            await RollbackAndDisposeAsync(transaction);
            throw;
        }
    }

    private async Task<CheckpointDatabaseLease?> BeginCheckpointDeleteLeaseAsync(
        int translationRequestId,
        string path,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        if (_dbContext == null)
        {
            return null;
        }

        var transaction = _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var request = await _dbContext.TranslationRequests
                .AsNoTracking()
                .Where(item => item.Id == translationRequestId)
                .Select(item => new
                {
                    item.Status,
                    item.JobId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (request == null)
            {
                return new CheckpointDatabaseLease(transaction, canProceed: true);
            }

            var hasExactActiveOwnership =
                !string.IsNullOrWhiteSpace(ownershipToken) &&
                request.Status == TranslationStatus.InProgress &&
                string.Equals(request.JobId, ownershipToken, StringComparison.Ordinal);
            var isOwnerlessActiveRequest =
                string.IsNullOrWhiteSpace(ownershipToken) &&
                request.Status is TranslationStatus.Pending or
                    TranslationStatus.InProgress or
                    TranslationStatus.Paused;
            var isTerminalRequest = request.Status is TranslationStatus.Completed or
                TranslationStatus.Failed or
                TranslationStatus.Cancelled or
                TranslationStatus.Interrupted;

            if (!hasExactActiveOwnership && isOwnerlessActiveRequest)
            {
                _logger.LogInformation(
                    "Skipped deletion of checkpoint for active ownerless request {RequestId}",
                    translationRequestId);
                return new CheckpointDatabaseLease(transaction, canProceed: false);
            }

            if (!hasExactActiveOwnership &&
                (!isTerminalRequest || !string.IsNullOrWhiteSpace(request.JobId)))
            {
                if (!string.IsNullOrWhiteSpace(ownershipToken))
                {
                    throw CreateOwnershipLostException(
                        translationRequestId,
                        ownershipToken,
                        cancellationToken);
                }

                return new CheckpointDatabaseLease(transaction, canProceed: false);
            }

            var rowsUpdated = await _dbContext.TranslationRequests
                .Where(item => item.Id == translationRequestId &&
                               item.Status == request.Status &&
                               item.JobId == request.JobId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
            if (rowsUpdated == 0)
            {
                if (!string.IsNullOrWhiteSpace(ownershipToken))
                {
                    throw CreateOwnershipLostException(
                        translationRequestId,
                        ownershipToken,
                        cancellationToken);
                }

                return new CheckpointDatabaseLease(transaction, canProceed: false);
            }

            if (!hasExactActiveOwnership && !string.IsNullOrWhiteSpace(ownershipToken))
            {
                var checkpoint = await ReadCheckpointAsync(
                    path,
                    translationRequestId,
                    sourceFingerprint: null,
                    cancellationToken);
                if (checkpoint?.OwnershipToken is { Length: > 0 } checkpointOwner &&
                    !string.Equals(checkpointOwner, ownershipToken, StringComparison.Ordinal))
                {
                    throw CreateOwnershipLostException(
                        translationRequestId,
                        ownershipToken,
                        cancellationToken);
                }
            }

            return new CheckpointDatabaseLease(transaction, canProceed: true);
        }
        catch
        {
            if (transaction != null)
            {
                await RollbackAndDisposeAsync(transaction);
            }

            throw;
        }
    }

    private static async Task RollbackAndDisposeAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private OperationCanceledException CreateOwnershipLostException(
        int translationRequestId,
        string ownershipToken,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Rejected checkpoint operation for request {RequestId} because attempt ownership {OwnershipToken} was lost",
            translationRequestId,
            ownershipToken);
        return new OperationCanceledException(
            $"Translation attempt ownership was lost for request {translationRequestId}.",
            cancellationToken);
    }

    private async Task WriteCheckpointAtomicallyAsync(
        string path,
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken,
        Func<Task>? beforeCommitAsync = null)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (beforeCommitAsync != null)
            {
                await beforeCommitAsync();
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to remove temporary translation checkpoint file {Path}", tempPath);
                }
            }
        }
    }

    private async Task EnsureAttemptOwnershipAsync(
        int translationRequestId,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dbContext == null || string.IsNullOrWhiteSpace(ownershipToken))
        {
            return;
        }

        var ownsAttempt = await _dbContext.TranslationRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Id == translationRequestId &&
                           request.Status == TranslationStatus.InProgress &&
                           request.JobId == ownershipToken,
                cancellationToken);

        if (ownsAttempt)
        {
            return;
        }

        _logger.LogInformation(
            "Rejected checkpoint write for request {RequestId} because attempt ownership {OwnershipToken} was lost",
            translationRequestId,
            ownershipToken);
        throw new OperationCanceledException(
            $"Translation attempt ownership was lost for request {translationRequestId}.",
            cancellationToken);
    }

    private static SemaphoreSlim GetCheckpointLock(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _checkpointLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
    }

    private string GetCheckpointPath(int translationRequestId)
    {
        return Path.Combine(_checkpointRoot, $"{translationRequestId}.json");
    }

    private async Task<TranslationCheckpoint?> ReadCheckpointAsync(
        string path,
        int translationRequestId,
        string? sourceFingerprint,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var checkpoint = await JsonSerializer.DeserializeAsync<TranslationCheckpoint>(
                stream,
                _jsonOptions,
                cancellationToken);

            if (checkpoint != null)
            {
                checkpoint.Translations ??= new Dictionary<int, string>();
                checkpoint.SourcePreservedPositions ??= [];
                checkpoint.SourcePreservedPositions.RemoveWhere(
                    position => !checkpoint.Translations.ContainsKey(position));
            }

            if (checkpoint == null ||
                (sourceFingerprint != null &&
                 !string.Equals(checkpoint.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal)))
            {
                return null;
            }

            return checkpoint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load translation checkpoint for request {RequestId}", translationRequestId);
            return null;
        }
    }

    private TranslationCheckpoint MergeFullCheckpoint(
        TranslationCheckpoint incomingCheckpoint,
        TranslationCheckpoint? currentCheckpoint)
    {
        var hasBaseline = _loadedCheckpointSnapshots.TryGetValue(
            incomingCheckpoint,
            out var baseline);
        if (hasBaseline &&
            baseline != null &&
            !string.Equals(
                baseline.SourceFingerprint,
                incomingCheckpoint.SourceFingerprint,
                StringComparison.Ordinal))
        {
            return CreateSourceResetCheckpoint(incomingCheckpoint);
        }

        if (currentCheckpoint == null ||
            currentCheckpoint.TranslationRequestId != incomingCheckpoint.TranslationRequestId ||
            !string.Equals(
                currentCheckpoint.SourceFingerprint,
                incomingCheckpoint.SourceFingerprint,
                StringComparison.Ordinal) ||
            !hasBaseline ||
            baseline == null)
        {
            return CloneCheckpoint(incomingCheckpoint);
        }

        var mergedCheckpoint = CloneCheckpoint(currentCheckpoint);

        foreach (var translation in incomingCheckpoint.Translations)
        {
            if (!baseline.Translations.TryGetValue(translation.Key, out var baselineTranslation) ||
                !string.Equals(baselineTranslation, translation.Value, StringComparison.Ordinal))
            {
                mergedCheckpoint.Translations[translation.Key] = translation.Value;
            }
        }

        foreach (var baselineTranslation in baseline.Translations)
        {
            if (incomingCheckpoint.Translations.ContainsKey(baselineTranslation.Key) ||
                !currentCheckpoint.Translations.TryGetValue(baselineTranslation.Key, out var currentTranslation) ||
                !string.Equals(currentTranslation, baselineTranslation.Value, StringComparison.Ordinal))
            {
                continue;
            }

            mergedCheckpoint.Translations.Remove(baselineTranslation.Key);
        }

        foreach (var position in incomingCheckpoint.SourcePreservedPositions)
        {
            if (!baseline.SourcePreservedPositions.Contains(position))
            {
                mergedCheckpoint.SourcePreservedPositions.Add(position);
            }
        }

        foreach (var position in baseline.SourcePreservedPositions)
        {
            if (incomingCheckpoint.SourcePreservedPositions.Contains(position) ||
                !currentCheckpoint.SourcePreservedPositions.Contains(position))
            {
                continue;
            }

            mergedCheckpoint.SourcePreservedPositions.Remove(position);
        }

        NormalizeSourcePreservedPositions(mergedCheckpoint);
        return mergedCheckpoint;
    }

    private static TranslationCheckpoint CreateSourceResetCheckpoint(
        TranslationCheckpoint checkpoint)
    {
        var resetCheckpoint = CloneCheckpoint(checkpoint);
        resetCheckpoint.Translations.Clear();
        resetCheckpoint.SourcePreservedPositions.Clear();
        return resetCheckpoint;
    }

    private static void NormalizeSourcePreservedPositions(
        TranslationCheckpoint checkpoint)
    {
        checkpoint.SourcePreservedPositions.RemoveWhere(
            position => !checkpoint.Translations.ContainsKey(position));
    }

    private sealed class CheckpointOwnershipLease : IAsyncDisposable
    {
        private readonly IDbContextTransaction _transaction;
        private bool _committed;

        public CheckpointOwnershipLease(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public async Task CommitAsync()
        {
            await _transaction.CommitAsync(CancellationToken.None);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                try
                {
                    await _transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            await _transaction.DisposeAsync();
        }
    }

    private sealed class CheckpointDatabaseLease : IAsyncDisposable
    {
        private readonly IDbContextTransaction? _transaction;
        private bool _committed;

        public CheckpointDatabaseLease(
            IDbContextTransaction? transaction,
            bool canProceed)
        {
            _transaction = transaction;
            CanProceed = canProceed;
        }

        public bool CanProceed { get; }

        public async Task CommitAsync()
        {
            if (_transaction == null)
            {
                return;
            }

            await _transaction.CommitAsync(CancellationToken.None);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction == null)
            {
                return;
            }

            if (!_committed)
            {
                try
                {
                    await _transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            await _transaction.DisposeAsync();
        }
    }

    private void TrackLoadedCheckpoint(TranslationCheckpoint? checkpoint)
    {
        if (checkpoint == null)
        {
            return;
        }

        _loadedCheckpointSnapshots.Remove(checkpoint);
        _loadedCheckpointSnapshots.Add(
            checkpoint,
            new LoadedCheckpointSnapshot
            {
                SourceFingerprint = checkpoint.SourceFingerprint,
                Translations = new Dictionary<int, string>(checkpoint.Translations),
                SourcePreservedPositions = [.. checkpoint.SourcePreservedPositions]
            });
    }

    private static TranslationCheckpoint CloneCheckpoint(TranslationCheckpoint checkpoint)
    {
        var translations = new Dictionary<int, string>(checkpoint.Translations);
        return new TranslationCheckpoint
        {
            TranslationRequestId = checkpoint.TranslationRequestId,
            SourceFingerprint = checkpoint.SourceFingerprint,
            OwnershipToken = checkpoint.OwnershipToken,
            Translations = translations,
            SourcePreservedPositions = checkpoint.SourcePreservedPositions
                .Where(translations.ContainsKey)
                .ToHashSet(),
            UpdatedAtUtc = checkpoint.UpdatedAtUtc
        };
    }
}
