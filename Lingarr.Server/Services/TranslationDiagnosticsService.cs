using System.Text.Json;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class TranslationDiagnosticsService : ITranslationDiagnosticsService
{
    private const int RetentionDays = 7;
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<TranslationDiagnosticsService> _logger;

    public TranslationDiagnosticsService(
        LingarrDbContext dbContext,
        ILogger<TranslationDiagnosticsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<TranslationDiagnosticEvent> RecordAsync(
        TranslationDiagnosticEventRequest request,
        CancellationToken cancellationToken)
    {
        var diagnosticEvent = new TranslationDiagnosticEvent
        {
            TranslationRequestId = request.TranslationRequestId,
            MediaId = request.MediaId,
            MediaType = request.MediaType,
            Title = request.Title,
            Stage = request.Stage,
            Provider = request.Provider,
            SourcePath = request.SourcePath,
            TargetPath = request.TargetPath,
            QuarantinePath = request.QuarantinePath,
            OutputFormat = request.OutputFormat,
            SourceSnapshotIdentity = request.SourceSnapshotIdentity,
            SourceSnapshotFingerprint = request.SourceSnapshotFingerprint,
            ReasonCode = request.ReasonCode,
            Summary = request.Summary,
            SampleLinesJson = JsonSerializer.Serialize(request.SampleLines),
            DetailsJson = request.DetailsJson,
            ExpiresAt = DateTime.UtcNow.AddDays(RetentionDays)
        };

        _dbContext.TranslationDiagnosticEvents.Add(diagnosticEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return diagnosticEvent;
    }

    public async Task<IReadOnlyList<TranslationDiagnosticEvent>> GetEventsAsync(
        int pageSize,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        pageNumber = Math.Max(1, pageNumber);

        return await _dbContext.TranslationDiagnosticEvents
            .OrderByDescending(e => e.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationDiagnosticEvent>> GetForRequestAsync(
        int requestId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.TranslationDiagnosticEvents
            .Where(e => e.TranslationRequestId == requestId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationDiagnosticEvent>> GetForMediaAsync(
        MediaType mediaType,
        int mediaId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.TranslationDiagnosticEvents
            .Where(e => e.MediaType == mediaType && e.MediaId == mediaId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public string CreateQuarantinePath(int translationRequestId, string finalPath)
    {
        var root = ResolveQuarantineRootPath(
            Environment.GetEnvironmentVariable("LINGARR_TRANSLATION_QUARANTINE_PATH"));

        var fileName = Path.GetFileName(finalPath);
        var safeFileName = string.Join(
            "_",
            fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(
            root,
            DateTime.UtcNow.ToString("yyyyMMdd"),
            translationRequestId.ToString(),
            $"{Guid.NewGuid():N}.{safeFileName}");
    }

    internal static string ResolveQuarantineRootPath(string? configuredRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return Path.GetFullPath(configuredRootPath);
        }

        if (!OperatingSystem.IsWindows() && Directory.Exists("/app/config"))
        {
            return "/app/config/translation-quarantine";
        }

        return Path.Combine(AppContext.BaseDirectory, "config", "translation-quarantine");
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expiredEvents = await _dbContext.TranslationDiagnosticEvents
            .Where(e => e.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var diagnosticEvent in expiredEvents)
        {
            if (!string.IsNullOrWhiteSpace(diagnosticEvent.QuarantinePath) &&
                File.Exists(diagnosticEvent.QuarantinePath))
            {
                try
                {
                    File.Delete(diagnosticEvent.QuarantinePath);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Failed to delete expired quarantine artifact {Path}",
                        diagnosticEvent.QuarantinePath);
                }
            }

            _dbContext.TranslationDiagnosticEvents.Remove(diagnosticEvent);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return expiredEvents.Count;
    }
}
