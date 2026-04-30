using System.Text.Json;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class SubtitleQualityAuditJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly ISubtitleQualityValidatorService _qualityValidatorService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly IHubContext<JobProgressHub> _hubContext;
    private readonly ILogger<SubtitleQualityAuditJob> _logger;

    public SubtitleQualityAuditJob(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISourceSubtitleResolver sourceSubtitleResolver,
        ISubtitleQualityValidatorService qualityValidatorService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        IHubContext<JobProgressHub> hubContext,
        ILogger<SubtitleQualityAuditJob> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _qualityValidatorService = qualityValidatorService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var stats = new SubtitleQualityAuditStats { IsRunning = true };
        SubtitleQualityAuditStats.Current = stats;

        var result = new SubtitleQualityAuditResult();
        try
        {
            var completedRequests = await _dbContext.TranslationRequests
                .Where(request => request.Status == TranslationStatus.Completed)
                .Where(request => request.MediaId != null)
                .Where(request => request.SubtitleToTranslate != null)
                .ToListAsync();

            var queuedRequests = await _dbContext.TranslationRequests
                .Where(request => request.Status == TranslationStatus.Pending ||
                                  request.Status == TranslationStatus.InProgress)
                .Where(request => request.MediaId != null)
                .Select(request => new { request.MediaType, request.MediaId })
                .ToListAsync();

            var queuedKeys = queuedRequests
                .Select(request => $"{request.MediaType}:{request.MediaId}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            stats.Total = completedRequests.Count;
            await SendProgress(stats);

            foreach (var request in completedRequests)
            {
                try
                {
                    await AuditRequestAsync(request, queuedKeys, result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Quality audit failed for translation request {RequestId}",
                        request.Id);
                }
                finally
                {
                    stats.ProcessedCount++;
                    if (stats.ProcessedCount % 10 == 0)
                    {
                        await SendProgress(stats);
                    }
                }
            }

            result.CompletedRequestsScanned = completedRequests.Count;
            result.CompletedAt = DateTime.UtcNow;
            stats.IsComplete = true;
            stats.IsRunning = false;
            await _settingService.SetSetting(
                SettingKeys.SubtitleValidation.LastQualityAuditResult,
                JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await SendProgress(stats);
        }
        catch (Exception ex)
        {
            stats.IsComplete = true;
            stats.IsRunning = false;
            stats.Error = ex.Message;
            await SendProgress(stats);
            throw;
        }
    }

    private async Task AuditRequestAsync(
        TranslationRequest request,
        HashSet<string> queuedKeys,
        SubtitleQualityAuditResult result)
    {
        if (request.MediaId == null)
        {
            return;
        }

        var sourcePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
            request,
            CancellationToken.None);
        var outputPaths = GetGeneratedOutputPaths(request);
        if (outputPaths.Count == 0 && !string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            outputPaths.Add(request.TranslatedSubtitle);
        }

        foreach (var outputPath in outputPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            {
                result.MissingOutputs++;
                AddFinding(
                    request,
                    sourcePath ?? request.SubtitleToTranslate ?? string.Empty,
                    outputPath ?? string.Empty,
                    request.SourceSubtitleFormat,
                    queuedKeys,
                    result,
                    new SubtitleQualityValidationResult
                    {
                        IsValid = false,
                        IssueTypes = [SubtitleQualityIssueCodes.MissingTarget],
                        Summary = "Generated subtitle output is missing from disk."
                    });
                continue;
            }

            result.FilesScanned++;

            if (_embeddedSubtitleCacheService.IsManagedCachePath(outputPath))
            {
                result.CacheOnlyOutputs++;
                AddFinding(
                    request,
                    sourcePath ?? request.SubtitleToTranslate ?? string.Empty,
                    outputPath,
                    Path.GetExtension(outputPath),
                    queuedKeys,
                    result,
                    new SubtitleQualityValidationResult
                    {
                        IsValid = false,
                        IssueTypes = [SubtitleQualityIssueCodes.CacheOnlyOutput],
                        Summary = "Generated subtitle output points at the embedded-subtitle cache instead of the media folder."
                    });
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                AddFinding(
                    request,
                    sourcePath ?? request.SubtitleToTranslate ?? string.Empty,
                    outputPath,
                    Path.GetExtension(outputPath),
                    queuedKeys,
                    result,
                    new SubtitleQualityValidationResult
                    {
                        IsValid = false,
                        IssueTypes = [SubtitleQualityIssueCodes.MissingSource],
                        Summary = "Source subtitle could not be resolved for audit."
                    });
                continue;
            }

            var validationResult = await _qualityValidatorService.ValidateAsync(
                new SubtitleQualityValidationRequest
                {
                    SourcePath = sourcePath,
                    TargetPath = outputPath,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    OutputFormat = Path.GetExtension(outputPath)
                },
                CancellationToken.None);

            if (!validationResult.IsValid)
            {
                AddFinding(
                    request,
                    sourcePath,
                    outputPath,
                    Path.GetExtension(outputPath),
                    queuedKeys,
                    result,
                    validationResult);
            }
        }
    }

    private static void AddFinding(
        TranslationRequest request,
        string sourcePath,
        string targetPath,
        string? outputFormat,
        HashSet<string> queuedKeys,
        SubtitleQualityAuditResult result,
        SubtitleQualityValidationResult validationResult)
    {
        result.Findings.Add(new SubtitleQualityAuditFinding
        {
            TranslationRequestId = request.Id,
            MediaId = request.MediaId ?? 0,
            MediaType = request.MediaType.ToString(),
            MediaTitle = request.Title,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            OutputFormat = outputFormat,
            SourceEntryCount = validationResult.SourceEntryCount,
            TargetEntryCount = validationResult.TargetEntryCount,
            MinimumTargetEntryCount = validationResult.MinimumTargetEntryCount,
            IssueTypes = validationResult.IssueTypes,
            IssueSummary = validationResult.Summary,
            SampleLines = validationResult.SampleLines,
            IsQueued = queuedKeys.Contains($"{request.MediaType}:{request.MediaId}")
        });
    }

    private static List<string> GetGeneratedOutputPaths(TranslationRequest request)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
        {
            paths.Add(request.TranslatedSubtitle);
        }

        if (!string.IsNullOrWhiteSpace(request.GeneratedSubtitlePaths))
        {
            try
            {
                paths.AddRange(JsonSerializer.Deserialize<List<string>>(request.GeneratedSubtitlePaths) ?? []);
            }
            catch (JsonException)
            {
                paths.Add(request.GeneratedSubtitlePaths);
            }
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task SendProgress(SubtitleQualityAuditStats stats)
    {
        try
        {
            await _hubContext.Clients.Group("JobProgress")
                .SendAsync("SubtitleQualityAuditProgress", stats);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send subtitle quality audit progress update");
        }
    }
}

public class SubtitleQualityAuditStats
{
    public static SubtitleQualityAuditStats? Current { get; set; }

    public int Total { get; set; }
    public int ProcessedCount { get; set; }
    public bool IsComplete { get; set; }
    public bool IsRunning { get; set; }
    public string? Error { get; set; }

    public double ProgressPercent => Total > 0 ? (double)ProcessedCount / Total * 100 : 0;
}
