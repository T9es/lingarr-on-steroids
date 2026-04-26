using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Services.Translation;

public class TranslationCheckpointService : ITranslationCheckpointService
{
    private readonly ILogger<TranslationCheckpointService> _logger;
    private readonly string _checkpointRoot;
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
    {
        _logger = logger;
        _checkpointRoot = checkpointRoot;
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

    public async Task SaveTranslationAsync(
        int translationRequestId,
        string sourceFingerprint,
        int position,
        string translatedText,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_checkpointRoot);
        var checkpoint = await LoadAsync(translationRequestId, sourceFingerprint, cancellationToken) ??
                         new TranslationCheckpoint
                         {
                             TranslationRequestId = translationRequestId,
                             SourceFingerprint = sourceFingerprint
                         };

        checkpoint.Translations[position] = translatedText;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;

        var path = GetCheckpointPath(translationRequestId);
        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, checkpoint, _jsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(int translationRequestId, CancellationToken cancellationToken)
    {
        var path = GetCheckpointPath(translationRequestId);
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

        return Task.CompletedTask;
    }

    private string GetCheckpointPath(int translationRequestId)
    {
        return Path.Combine(_checkpointRoot, $"{translationRequestId}.json");
    }
}
