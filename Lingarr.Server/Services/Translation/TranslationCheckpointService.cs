using System.Collections.Concurrent;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Services.Translation;

public class TranslationCheckpointService : ITranslationCheckpointService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _checkpointLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<TranslationCheckpointService> _logger;
    private readonly string _checkpointRoot;
    private readonly Func<Task>? _beforeCheckpointWriteAsync;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger)
        : this(logger, Path.Combine(AppContext.BaseDirectory, "config", "translation-checkpoints"))
    {
    }

    internal TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        string checkpointRoot)
        : this(logger, checkpointRoot, null)
    {
    }

    internal TranslationCheckpointService(
        ILogger<TranslationCheckpointService> logger,
        string checkpointRoot,
        Func<Task>? beforeCheckpointWriteAsync)
    {
        _logger = logger;
        _checkpointRoot = checkpointRoot;
        _beforeCheckpointWriteAsync = beforeCheckpointWriteAsync;
    }

    public async Task<TranslationCheckpoint?> LoadAsync(
        int translationRequestId,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
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

            if (checkpoint == null ||
                !string.Equals(checkpoint.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
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

    /// <inheritdoc />
    public async Task<TranslationCheckpoint?> LoadByRequestIdAsync(
        int translationRequestId,
        CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TranslationCheckpoint>(
                stream,
                _jsonOptions,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load translation checkpoint for request {RequestId}", translationRequestId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_checkpointRoot);
        var path = GetCheckpointPath(checkpoint.TranslationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
            await WriteCheckpointAtomicallyAsync(path, checkpoint, cancellationToken);
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    public async Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
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

            checkpoint.Translations[position] = translatedText;
            checkpoint.UpdatedAtUtc = DateTime.UtcNow;
            await WriteCheckpointAtomicallyAsync(path, checkpoint, cancellationToken);
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    public async Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
        var checkpointLock = GetCheckpointLock(path);
        await checkpointLock.WaitAsync(cancellationToken);
        try
        {
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
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task WriteCheckpointAtomicallyAsync(
        string path,
        TranslationCheckpoint checkpoint,
        CancellationToken cancellationToken)
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

    private static SemaphoreSlim GetCheckpointLock(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _checkpointLocks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
    }

    private string GetCheckpointPath(int translationRequestId)
    {
        return Path.Combine(_checkpointRoot, $"{translationRequestId}.json");
    }
}
