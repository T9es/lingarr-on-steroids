using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;
using SubtitleValidationOptions = Lingarr.Server.Models.SubtitleValidationOptions;

namespace Lingarr.Server.Jobs;

public class TranslationJob
{
    private readonly ILogger<TranslationJob> _logger;
    private readonly ISettingService _settings;
    private readonly LingarrDbContext _dbContext;
    private readonly IProgressService _progressService;
    private readonly ISubtitleService _subtitleService;
    private readonly IScheduleService _scheduleService;
    private readonly IStatisticsService _statisticsService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly IParallelTranslationLimiter _parallelLimiter;
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ITranslationCancellationService _cancellationService;

    public TranslationJob(
        ILogger<TranslationJob> logger,
        ISettingService settings,
        LingarrDbContext dbContext,
        IProgressService progressService,
        ISubtitleService subtitleService,
        IScheduleService scheduleService,
        IStatisticsService statisticsService,
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        IParallelTranslationLimiter parallelLimiter,
        IBatchFallbackService batchFallbackService,
        ISubtitleExtractionService extractionService,
        ITranslationCancellationService cancellationService)
    {
        _logger = logger;
        _settings = settings;
        _dbContext = dbContext;
        _progressService = progressService;
        _subtitleService = subtitleService;
        _scheduleService = scheduleService;
        _statisticsService = statisticsService;
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _parallelLimiter = parallelLimiter;
        _batchFallbackService = batchFallbackService;
        _extractionService = extractionService;
        _cancellationService = cancellationService;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("translation")]
    public async Task Execute(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        var requestLogs = new List<TranslationRequestLog>();

        void AddRequestLog(string level, string message, string? details = null)
        {
            requestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = translationRequest.Id,
                Level = level,
                Message = message,
                Details = details
            });
        }

        var jobName = JobContextFilter.GetCurrentJobTypeName();
        var jobId = JobContextFilter.GetCurrentJobId();

        // Register this job for cooperative cancellation and create a linked token
        var jobCancellationToken = _cancellationService.RegisterJob(translationRequest.Id);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, jobCancellationToken);
        var effectiveCancellationToken = linkedCts.Token;
        
        // Acquire a parallel translation slot (blocks if limit reached)
        using var slot = await _parallelLimiter.AcquireAsync(effectiveCancellationToken);

        
        string? temporaryFilePath = null;
        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
            effectiveCancellationToken.ThrowIfCancellationRequested();

            var request = await _translationRequestService.UpdateTranslationRequest(
                translationRequest,
                TranslationStatus.InProgress,
                jobId);

            var subtitlePathForLog = translationRequest.SubtitleToTranslate ?? "Unknown";
            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                subtitlePathForLog);
            AddRequestLog("Information", $"TranslateJob started for subtitle: {subtitlePathForLog}");

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.FixOverlappingSubtitles,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.AddTranslatorInfo,

                SettingKeys.SubtitleValidation.ValidateSubtitles,
                SettingKeys.SubtitleValidation.MaxFileSizeBytes,
                SettingKeys.SubtitleValidation.MaxSubtitleLength,
                SettingKeys.SubtitleValidation.MinSubtitleLength,
                SettingKeys.SubtitleValidation.MinDurationMs,
                SettingKeys.SubtitleValidation.MaxDurationSecs,

                SettingKeys.Translation.AiContextPromptEnabled,
                SettingKeys.Translation.AiContextBefore,
                SettingKeys.Translation.AiContextBefore,
                SettingKeys.Translation.AiContextAfter,
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.RemoveLanguageTag,
                SettingKeys.Translation.UseSubtitleTagging,
                SettingKeys.Translation.SubtitleTag,
                SettingKeys.Translation.EnableBatchFallback,
                SettingKeys.Translation.MaxBatchSplitAttempts,
                SettingKeys.Translation.StripAssDrawingCommands,
                SettingKeys.Translation.CleanSourceAssDrawings
            ]);
            var serviceType = settings[SettingKeys.Translation.ServiceType];
            var stripSubtitleFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
            var addTranslatorInfo = settings[SettingKeys.Translation.AddTranslatorInfo] == "true";
            var validateSubtitles = settings[SettingKeys.SubtitleValidation.ValidateSubtitles] != "false";
            var removeLanguageTag = settings[SettingKeys.Translation.RemoveLanguageTag] != "false";

            AddRequestLog(
                "Information",
                $"Settings: serviceType={serviceType}, stripFormatting={stripSubtitleFormatting}, addTranslatorInfo={addTranslatorInfo}, validateSubtitles={validateSubtitles}, removeLanguageTag={removeLanguageTag}");

            var contextBefore = 0;
            var contextAfter = 0;
            if (settings[SettingKeys.Translation.AiContextPromptEnabled] == "true")
            {
                contextBefore = int.TryParse(settings[SettingKeys.Translation.AiContextBefore],
                    out var linesBefore)
                    ? linesBefore
                    : 0;
                contextAfter = int.TryParse(settings[SettingKeys.Translation.AiContextAfter],
                    out var linesAfter)
                    ? linesAfter
                    : 0;
            }

            // AUTO-EXTRACTION: If subtitle file doesn't exist, check for embedded subtitles
            var subtitlePath = request.SubtitleToTranslate;
            if (string.IsNullOrEmpty(subtitlePath) || !File.Exists(subtitlePath))
            {
                _logger.LogInformation("Subtitle file not found, checking for embedded subtitles...");
                AddRequestLog("Warning", "Subtitle file not found on disk, attempting embedded subtitle extraction");
                subtitlePath = await TryExtractEmbeddedSubtitle(request);
                
                if (string.IsNullOrEmpty(subtitlePath))
                {
                    var errorMessage =
                        $"Subtitle file not found and no extractable embedded subtitle available: {request.SubtitleToTranslate}";
                    _logger.LogError(errorMessage);
                    AddRequestLog("Error", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }
                
                // Update the request with the extracted subtitle path
                request.SubtitleToTranslate = subtitlePath;
                _logger.LogInformation("Using extracted embedded subtitle: {Path}", subtitlePath);
                AddRequestLog("Information", $"Using extracted embedded subtitle: {subtitlePath}");
                temporaryFilePath = subtitlePath;
            }

            // validate subtitles
            if (validateSubtitles)
            {
                var validationOptions = new SubtitleValidationOptions
                {
                    // File size setting - default to 2MB if parsing fails
                    MaxFileSizeBytes = long.TryParse(settings[SettingKeys.SubtitleValidation.MaxFileSizeBytes],
                        out var maxFileSizeBytes)
                        ? maxFileSizeBytes
                        : 2 * 1024 * 1024,

                    // Maximum characters per subtitle - default to 500 if parsing fails
                    MaxSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MaxSubtitleLength],
                        out var maxSubtitleLength)
                        ? maxSubtitleLength
                        : 500,

                    // Minimum characters per subtitle - default to 1 if parsing fails
                    MinSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MinSubtitleLength],
                        out var minSubtitleLength)
                        ? minSubtitleLength
                        : 2,

                    // Minimum duration in milliseconds - default to 500ms if parsing fails
                    MinDurationMs = double.TryParse(settings[SettingKeys.SubtitleValidation.MinDurationMs],
                        out var minDurationMs)
                        ? minDurationMs
                        : 500,

                    // Maximum duration in seconds - default to 10s if parsing fails
                    MaxDurationSecs = double.TryParse(settings[SettingKeys.SubtitleValidation.MaxDurationSecs],
                        out var maxDurationSecs)
                        ? maxDurationSecs
                        : 10,

                    // Used to determine content length when
                    StripSubtitleFormatting = stripSubtitleFormatting
                };

                if (!_subtitleService.ValidateSubtitle(request.SubtitleToTranslate, validationOptions))
                {
                    const string validationMessage = "Subtitle is not valid according to configured preferences.";
                    _logger.LogWarning(validationMessage);
                    AddRequestLog("Warning", validationMessage);
                    throw new TaskCanceledException(validationMessage);
                }

                var isValid = _subtitleService.ValidateSubtitle(
                    request.SubtitleToTranslate,
                    validationOptions);

                if (!isValid)
                {
                    const string validationMessage = "Subtitle is not valid according to configured preferences.";
                    _logger.LogWarning(validationMessage);
                    AddRequestLog("Warning", validationMessage);
                    throw new TaskCanceledException(validationMessage);
                }
            }

            // translate subtitles
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
            var translator = new SubtitleTranslationService(
                translationService,
                _logger,
                _progressService,
                _batchFallbackService);
            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
            AddRequestLog("Information", $"Loaded subtitle file with {subtitles.Count} entries for translation");

            // If the subtitle file parsed to zero entries, attempt a fallback to embedded subtitles (if available)
            if (subtitles.Count == 0 && request.MediaId != null && string.IsNullOrEmpty(temporaryFilePath))
            {
                var emptyMessage =
                    $"Subtitle file appears to contain no readable entries: {request.SubtitleToTranslate}. Attempting embedded subtitle extraction as fallback.";
                _logger.LogWarning(emptyMessage);
                AddRequestLog("Warning", emptyMessage);

                var fallbackPath = await TryExtractEmbeddedSubtitle(request);
                if (!string.IsNullOrEmpty(fallbackPath))
                {
                    request.SubtitleToTranslate = fallbackPath;
                    temporaryFilePath = fallbackPath;

                    subtitles = await _subtitleService.ReadSubtitles(fallbackPath);
                    AddRequestLog("Information",
                        $"Loaded fallback embedded subtitle file with {subtitles.Count} entries for translation");
                }
            }
            
            // Parse batch fallback settings
            var enableBatchFallback = settings[SettingKeys.Translation.EnableBatchFallback] == "true";
            var maxBatchSplitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var splitAttempts)
                ? splitAttempts
                : 3;
            
            // Generate a short, readable identifier from the filename for logging
            // e.g., "S02E23" or "Movie Name (2024)"
            var fileIdentifier = GenerateFileIdentifier(request.SubtitleToTranslate);
            
            // Parse ASS drawing command filter settings
            var stripAssDrawingCommands = settings.TryGetValue(SettingKeys.Translation.StripAssDrawingCommands, out var stripAssVal) && stripAssVal == "true";
            var cleanSourceAssDrawings = settings.TryGetValue(SettingKeys.Translation.CleanSourceAssDrawings, out var cleanSourceVal) && cleanSourceVal == "true";
            
            // Filter out ASS drawing commands if enabled
            if (stripAssDrawingCommands)
            {
                var originalCount = subtitles.Count;
                subtitles = subtitles.Where(s => 
                {
                    var text = string.Join(" ", stripSubtitleFormatting ? s.PlaintextLines : s.Lines);
                    return !SubtitleFormatterService.IsAssDrawingCommand(text);
                }).ToList();
                
                var removedCount = originalCount - subtitles.Count;
                if (removedCount > 0)
                {
                    _logger.LogInformation(
                        "[{FileId}] Filtered out {RemovedCount} ASS drawing command entries from {OriginalCount} subtitles",
                        fileIdentifier, removedCount, originalCount);
                }
                
                // Optionally clean the source file as well
                if (cleanSourceAssDrawings && removedCount > 0)
                {
                    await CleanSourceSubtitleFile(request.SubtitleToTranslate, stripSubtitleFormatting);
                    _logger.LogInformation("[{FileId}] Cleaned ASS drawing commands from source file", fileIdentifier);
                    AddRequestLog("Information",
                        $"[{fileIdentifier}] Cleaned ASS drawing commands from source subtitle file");
                }
            }

            // If no subtitles remain after parsing and optional ASS drawing filtering, cancel gracefully
            if (subtitles.Count == 0)
            {
                var noContentMessage =
                    $"[{fileIdentifier}] Subtitle file has no subtitle entries remaining after parsing and filtering; cancelling translation.";
                _logger.LogWarning(noContentMessage);
                AddRequestLog("Warning", noContentMessage);
                throw new TaskCanceledException(noContentMessage);
            }
            
            List<SubtitleItem> translatedSubtitles;
            if (settings[SettingKeys.Translation.UseBatchTranslation] == "true"
                && translationService is IBatchTranslationService _)
            {
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize],
                    out var batchSize)
                    ? batchSize
                    : 10000;
                
                // Calculate effective batch size and total batches for upfront logging
                var effectiveBatchSize = maxSize <= 0 ? subtitles.Count : maxSize;
                var totalBatches = (int)Math.Ceiling((double)subtitles.Count / effectiveBatchSize);

                _logger.LogInformation(
                    "[{FileId}] Starting batch translation: {SubtitleCount} subtitles, {TotalBatches} batch(es) of {BatchSize}, fallback: {EnableFallback} ({SplitAttempts} attempts)",
                    fileIdentifier, subtitles.Count, totalBatches, effectiveBatchSize, enableBatchFallback, maxBatchSplitAttempts);

                AddRequestLog(
                    "Information",
                    $"[{fileIdentifier}] Starting batch translation: subtitles={subtitles.Count}, totalBatches={totalBatches}, batchSize={effectiveBatchSize}, fallback={enableBatchFallback}, maxSplitAttempts={maxBatchSplitAttempts}");

                translatedSubtitles = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    maxSize,
                    enableBatchFallback,
                    maxBatchSplitAttempts,
                    fileIdentifier,
                    effectiveCancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "[{FileId}] Starting individual translation: {SubtitleCount} subtitles, context (before: {ContextBefore}, after: {ContextAfter})",
                    fileIdentifier, subtitles.Count, contextBefore, contextAfter);
                AddRequestLog(
                    "Information",
                    $"[{fileIdentifier}] Starting individual translation: subtitles={subtitles.Count}, contextBefore={contextBefore}, contextAfter={contextAfter}");

                translatedSubtitles = await translator.TranslateSubtitles(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    contextBefore,
                    contextAfter,
                    effectiveCancellationToken
                );
            }

            if (settings[SettingKeys.Translation.FixOverlappingSubtitles] == "true")
            {
                translatedSubtitles = _subtitleService.FixOverlappingSubtitles(translatedSubtitles);
            }

            if (addTranslatorInfo)
            {
                _subtitleService.AddTranslatorInfo(serviceType, translatedSubtitles, translationService);
            }

            if (stripSubtitleFormatting && translatedSubtitles.Count > 0)
            {
                var format = translatedSubtitles[0].SsaFormat;
                if (format != null)
                {
                    format.Styles = [];
                }
            }

            // statistics tracking
            await _statisticsService.UpdateTranslationStatisticsFromSubtitles(request, serviceType, translatedSubtitles);

            var subtitleTag = "";
            if (settings[SettingKeys.Translation.UseSubtitleTagging] == "true")
            {
                subtitleTag = settings[SettingKeys.Translation.SubtitleTag];
            }

            await WriteSubtitles(request, translatedSubtitles, stripSubtitleFormatting, subtitleTag, removeLanguageTag);
            AddRequestLog("Information", "Translation completed successfully and subtitle file was written");
            await HandleCompletion(jobName, request, effectiveCancellationToken);
        }
        catch (TaskCanceledException)
        {
            await HandleCancellation(jobName, translationRequest);
        }
        catch (OperationCanceledException)
        {
            // Also catch OperationCanceledException for cooperative cancellation
            await HandleCancellation(jobName, translationRequest);
        }
        catch (Exception ex)
        {
            try
            {
                await _translationRequestService.ClearMediaHash(translationRequest);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Error clearing media hash during failure handling");
            }

            try 
            {
                translationRequest = await _translationRequestService.UpdateTranslationRequest(
                    translationRequest,
                    TranslationStatus.Failed,
                    jobId);

                // Persist collected logs for failed translations
                if (requestLogs.Count > 0)
                {
                    _dbContext.TranslationRequestLogs.AddRange(requestLogs);
                }

                // Add the failure entry as the final log message
                var failureMessage = $"Translation failed: {ex.Message}";
                _logger.LogError(ex, "Translation failed for request {RequestId}", translationRequest.Id);
                _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
                {
                    TranslationRequestId = translationRequest.Id,
                    Level = "Error",
                    Message = failureMessage,
                    Details = ex.ToString()
                });

                await _dbContext.SaveChangesAsync();

                await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
                await _translationRequestService.UpdateActiveCount();
                await _progressService.Emit(translationRequest, 0);
            }
            catch (DeepL.NotFoundException)
            {
                _logger.LogWarning("Validation request {RequestId} not found during failure handling - it was likely deleted", translationRequest.Id);
                // Swallow this as we can't update a missing request
            }
            catch (Exception stateEx)
            {
                _logger.LogError(stateEx, "Error updating job state during failure handling");
            }
            
            // Re-throw the original exception to ensure Hangfire knows the job failed
            throw;
        }
        finally
        {
            // Always unregister the job from cooperative cancellation
            _cancellationService.UnregisterJob(translationRequest.Id);
            
            if (!string.IsNullOrEmpty(temporaryFilePath) && File.Exists(temporaryFilePath))
            {
                try
                {
                    File.Delete(temporaryFilePath);
                    _logger.LogDebug("Deleted temporary extracted subtitle: {Path}", temporaryFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary extracted subtitle: {Path}", temporaryFilePath);
                }
            }
        }
    }

    private async Task WriteSubtitles(TranslationRequest translationRequest,
        List<SubtitleItem> translatedSubtitles,
        bool stripSubtitleFormatting,
        string subtitleTag,
        bool removeLanguageTag)
    {
        try
        {
            var targetLanguage = removeLanguageTag ? "" : translationRequest.TargetLanguage;

            var outputPath = _subtitleService.CreateFilePath(
                translationRequest.SubtitleToTranslate,
                targetLanguage,
                subtitleTag);

            await _subtitleService.WriteSubtitles(outputPath, translatedSubtitles, stripSubtitleFormatting);

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                outputPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private async Task HandleCompletion(
        string jobName,
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Completed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    private async Task HandleCancellation(string jobName, TranslationRequest request)
    {
        _logger.LogInformation("Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
            request.SubtitleToTranslate);
        var translationRequest =
            await _dbContext.TranslationRequests.FirstOrDefaultAsync(translationRequest =>
                translationRequest.Id == request.Id);

        if (translationRequest != null)
        {
            translationRequest.CompletedAt = DateTime.UtcNow;
            translationRequest.Status = TranslationStatus.Cancelled;

            await _dbContext.SaveChangesAsync();
            await _translationRequestService.ClearMediaHash(translationRequest);
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Cancelled.GetDisplayName());
        }
    }
    
    /// <summary>
    /// Generates a short, readable identifier from the subtitle file path for logging.
    /// Attempts to extract episode identifiers (e.g., "S02E23") or movie names.
    /// </summary>
    /// <param name="subtitlePath">The full path to the subtitle file</param>
    /// <returns>A short identifier suitable for log output</returns>
    private static string GenerateFileIdentifier(string? subtitlePath)
    {
        if (string.IsNullOrEmpty(subtitlePath)) return "Unknown";
        var fileName = Path.GetFileNameWithoutExtension(subtitlePath);
        
        // Try to find episode pattern (S01E01 or similar)
        var episodeMatch = System.Text.RegularExpressions.Regex.Match(
            fileName, 
            @"[Ss]\d{1,2}[Ee]\d{1,2}", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (episodeMatch.Success)
        {
            return episodeMatch.Value.ToUpperInvariant();
        }
        
        // For movies or other files, use first 30 chars of filename
        return fileName.Length > 30 ? fileName[..30] + "..." : fileName;
    }
    
    /// <summary>
    /// Cleans ASS drawing commands from the source subtitle file by reading, filtering, and rewriting it.
    /// </summary>
    /// <param name="subtitlePath">Path to the source subtitle file</param>
    /// <param name="stripSubtitleFormatting">Whether to use plaintext lines for detection</param>
    private async Task CleanSourceSubtitleFile(string subtitlePath, bool stripSubtitleFormatting)
    {
        try
        {
            // Read the original subtitles
            var subtitles = await _subtitleService.ReadSubtitles(subtitlePath);
            
            // Filter out ASS drawing commands
            var cleanedSubtitles = subtitles.Where(s =>
            {
                var text = string.Join(" ", stripSubtitleFormatting ? s.PlaintextLines : s.Lines);
                return !SubtitleFormatterService.IsAssDrawingCommand(text);
            }).ToList();
            
            // Only rewrite if we actually removed something
            if (cleanedSubtitles.Count < subtitles.Count)
            {
                // Reposition the subtitles after filtering
                for (int i = 0; i < cleanedSubtitles.Count; i++)
                {
                    cleanedSubtitles[i].Position = i + 1;
                }
                
                // Write the cleaned subtitles back to the original file
                await _subtitleService.WriteSubtitles(subtitlePath, cleanedSubtitles, stripSubtitleFormatting);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean source subtitle file: {Path}", subtitlePath);
            // Don't throw - this is a non-critical operation
        }
    }
    
    /// <summary>
    /// Attempts to find and extract an embedded subtitle from the media file.
    /// Iterates through configured source languages in priority order, selecting
    /// dialogue tracks (not Signs/Songs) when available.
    /// </summary>
    /// <param name="request">The translation request containing media ID and source language</param>
    /// <returns>Path to the extracted subtitle file, or null if no suitable embedded subtitle was found</returns>
    private async Task<string?> TryExtractEmbeddedSubtitle(TranslationRequest request)
    {
        if (request.MediaId == null)
        {
            _logger.LogWarning("Cannot extract embedded subtitle: MediaId is null");
            return null;
        }

        try
        {
            List<EmbeddedSubtitle>? embeddedSubtitles = null;
            string? mediaPath = null;
            string? outputDir = null;

            // Find the media and its embedded subtitles based on MediaType
            if (request.MediaType == MediaType.Episode)
            {
                var episode = await _dbContext.Episodes
                    .Include(e => e.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(e => e.Id == request.MediaId);

                if (episode == null)
                {
                    _logger.LogWarning("Episode not found: {MediaId}", request.MediaId);
                    return null;
                }

                if (string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
                {
                    _logger.LogWarning("Episode has no path/filename: {MediaId}", request.MediaId);
                    return null;
                }

                // Sync embedded subtitles if not already done
                if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
                {
                    await _extractionService.SyncEmbeddedSubtitles(episode);
                    await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                }

                embeddedSubtitles = episode.EmbeddedSubtitles;
                mediaPath = Path.Combine(episode.Path, episode.FileName);
                outputDir = episode.Path;
            }
            else if (request.MediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies
                    .Include(m => m.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(m => m.Id == request.MediaId);

                if (movie == null)
                {
                    _logger.LogWarning("Movie not found: {MediaId}", request.MediaId);
                    return null;
                }

                if (string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
                {
                    _logger.LogWarning("Movie has no path/filename: {MediaId}", request.MediaId);
                    return null;
                }

                // Sync embedded subtitles if not already done
                if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
                {
                    await _extractionService.SyncEmbeddedSubtitles(movie);
                    await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                }

                embeddedSubtitles = movie.EmbeddedSubtitles;
                mediaPath = Path.Combine(movie.Path, movie.FileName);
                outputDir = movie.Path;
            }
            else
            {
                _logger.LogWarning("Unsupported media type for embedded extraction: {MediaType}", request.MediaType);
                return null;
            }

            // Fetch ordered source languages from settings
            var sourceLanguages = await _settings.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
            var orderedSourceLanguageCodes = sourceLanguages.Select(l => l.Code).ToList();
            
            // Fetch skip Signs/Songs setting (defaults to true if not set)
            var skipSignsSongsSetting = await _settings.GetSetting(SettingKeys.SubtitleExtraction.SkipSignsSongs);
            var skipSignsSongs = skipSignsSongsSetting != "false"; // Default to true
            
            // Find best subtitle using multi-language priority
            var result = FindBestEmbeddedSubtitle(embeddedSubtitles, orderedSourceLanguageCodes, skipSignsSongs);
            
            if (!result.Success)
            {
                _logger.LogWarning(result.FailureReason);
                throw new InvalidOperationException(result.FailureReason);
            }
            
            var bestCandidate = result.Subtitle!;

            _logger.LogInformation(
                "Found embedded subtitle candidate: Stream {StreamIndex}, Language: {Language}, Title: {Title}, Codec: {Codec}",
                bestCandidate.StreamIndex, bestCandidate.Language ?? "unknown", bestCandidate.Title ?? "untitled", bestCandidate.CodecName);

            // Extract the subtitle
            var extractedPath = await _extractionService.ExtractSubtitle(
                mediaPath!,
                bestCandidate.StreamIndex,
                outputDir!,
                bestCandidate.CodecName,
                bestCandidate.Language);

            if (string.IsNullOrEmpty(extractedPath))
            {
                _logger.LogError("Failed to extract embedded subtitle stream {StreamIndex}", bestCandidate.StreamIndex);
                throw new InvalidOperationException($"Embedded subtitle extraction failed for stream {bestCandidate.StreamIndex}");
            }

            // Update the database record
            bestCandidate.IsExtracted = true;
            bestCandidate.ExtractedPath = extractedPath;
            await _dbContext.SaveChangesAsync();

            return extractedPath;
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw extraction failures
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during embedded subtitle extraction for media {MediaId}", request.MediaId);
            throw new InvalidOperationException($"Embedded subtitle extraction failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Result of embedded subtitle selection, including success status and failure reason.
    /// </summary>
    private sealed class SubtitleSelectionResult
    {
        public bool Success { get; init; }
        public EmbeddedSubtitle? Subtitle { get; init; }
        public string? FailureReason { get; init; }
        
        public static SubtitleSelectionResult Found(EmbeddedSubtitle subtitle) =>
            new() { Success = true, Subtitle = subtitle };
        
        public static SubtitleSelectionResult Failed(string reason) =>
            new() { Success = false, FailureReason = reason };
    }
    
    /// <summary>
    /// Finds the best embedded subtitle candidate for translation using multi-language priority.
    /// Iterates through configured source languages in order, optionally skipping Signs/Songs tracks.
    /// </summary>
    /// <param name="embeddedSubtitles">List of embedded subtitles from the media</param>
    /// <param name="orderedSourceLanguages">Ordered list of source language codes from settings</param>
    /// <param name="skipSignsSongs">When true, skip Signs/Songs/karaoke tracks; when false, allow them</param>
    /// <returns>Selection result with the best subtitle or failure reason</returns>
    private static SubtitleSelectionResult FindBestEmbeddedSubtitle(
        List<EmbeddedSubtitle>? embeddedSubtitles, 
        List<string> orderedSourceLanguages,
        bool skipSignsSongs)
    {
        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
        {
            return SubtitleSelectionResult.Failed("No embedded subtitles found in media file.");
        }

        // Only consider text-based subtitles
        var textBased = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
        if (textBased.Count == 0)
        {
            return SubtitleSelectionResult.Failed("No text-based embedded subtitles found. Only image-based (bitmap) subtitles available.");
        }

        // Track which languages only have Signs/Songs tracks (only relevant if skipping)
        var languagesWithOnlySignsSongs = new List<string>();
        
        // Try each configured source language in priority order
        foreach (var sourceLanguage in orderedSourceLanguages)
        {
            var languageMatched = textBased
                .Where(s => LanguageMatches(s.Language, sourceLanguage))
                .ToList();
            
            if (languageMatched.Count == 0)
            {
                continue; // No subtitles for this language, try next
            }
            
            // Score candidates
            var bestTrack = skipSignsSongs 
                ? FindBestDialogueTrack(languageMatched, sourceLanguage) 
                : FindBestTrackIncludingSignsSongs(languageMatched, sourceLanguage);
            
            if (bestTrack != null)
            {
                return SubtitleSelectionResult.Found(bestTrack);
            }
            
            // Only Signs/Songs for this language, track it and try next language
            languagesWithOnlySignsSongs.Add(sourceLanguage);
        }
        
        // No dialogue subtitle found in any configured language.
        // Check if there are dialogue subtitles in OTHER languages (not configured)
        var configuredLanguages = orderedSourceLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherLanguageSubs = textBased
            .Where(s => !string.IsNullOrEmpty(s.Language) && 
                        !configuredLanguages.Any(cfg => LanguageMatches(s.Language, cfg)))
            .ToList();
        
        // Check if any of those other language tracks have dialogue content
        var otherDialogueTracks = otherLanguageSubs
            .Where(s => !IsSignsSongsTrack(s))
            .ToList();
        
        if (otherDialogueTracks.Count > 0)
        {
            // Build actionable message
            var availableLanguages = otherDialogueTracks
                .Select(s => s.Language)
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .ToList();
            
            var trackInfo = string.Join(", ", otherDialogueTracks
                .Take(3)
                .Select(s => $"'{s.Title ?? "Untitled"}' ({s.Language})"));
            
            return SubtitleSelectionResult.Failed(
                $"No dialogue subtitles found for configured languages [{string.Join(", ", orderedSourceLanguages)}]. " +
                $"However, dialogue tracks are available in other languages: {trackInfo}. " +
                $"Add '{string.Join("' or '", availableLanguages)}' to your source languages if you want to use them.");
        }
        
        // Only Signs/Songs tracks available anywhere
        if (languagesWithOnlySignsSongs.Count > 0)
        {
            return SubtitleSelectionResult.Failed(
                $"Only Signs/Songs subtitle tracks found for configured languages [{string.Join(", ", languagesWithOnlySignsSongs)}]. " +
                "No full dialogue subtitles available for translation.");
        }
        
        return SubtitleSelectionResult.Failed(
            $"No subtitles match configured source languages [{string.Join(", ", orderedSourceLanguages)}]. " +
            $"Available languages: [{string.Join(", ", textBased.Select(s => s.Language ?? "unknown").Distinct())}].");
    }
    
    /// <summary>
    /// Finds the best dialogue track from a list of candidates, excluding Signs/Songs/karaoke.
    /// </summary>
    private static EmbeddedSubtitle? FindBestDialogueTrack(List<EmbeddedSubtitle> candidates, string sourceLanguage)
    {
        EmbeddedSubtitle? best = null;
        var bestScore = int.MinValue;
        
        foreach (var subtitle in candidates)
        {
            // Skip Signs/Songs/karaoke tracks entirely
            if (IsSignsSongsTrack(subtitle))
            {
                continue;
            }
            
            var score = ScoreDialogueCandidate(subtitle, sourceLanguage);
            
            if (score > bestScore ||
                (score == bestScore && best != null && subtitle.StreamIndex < best.StreamIndex))
            {
                bestScore = score;
                best = subtitle;
            }
        }
        
        return best;
    }
    
    /// <summary>
    /// Determines if a subtitle track is a Signs/Songs/karaoke-only track.
    /// </summary>
    private static bool IsSignsSongsTrack(EmbeddedSubtitle subtitle)
    {
        var title = subtitle.Title?.ToLowerInvariant() ?? string.Empty;
        
        // Check title for signs/songs indicators
        if (title.Contains("sign") || title.Contains("song") || title.Contains("karaoke"))
        {
            // But not if it also says "full" (e.g., "Full Subs + Songs")
            if (!title.Contains("full") && !title.Contains("dialog") && !title.Contains("dialogue"))
            {
                return true;
            }
        }
        
        // Forced tracks are often Signs/Songs
        if (subtitle.IsForced && !title.Contains("full") && !title.Contains("sub"))
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Scores a dialogue subtitle candidate based on title and flags.
    /// Higher scores indicate better candidates for full dialogue translation.
    /// </summary>
    private static int ScoreDialogueCandidate(EmbeddedSubtitle subtitle, string sourceLanguage)
    {
        var score = 0;
        
        // Language match bonus
        if (LanguageMatches(subtitle.Language, sourceLanguage))
        {
            score += 50;
        }
        
        var title = subtitle.Title?.ToLowerInvariant() ?? string.Empty;
        
        // Positive indicators for full dialogue tracks
        if (title.Contains("full"))
        {
            score += 25;
        }
        
        if (title.Contains("dialog") || title.Contains("dialogue"))
        {
            score += 20;
        }
        
        if (title.Contains("sub") || title.Contains("subtitle"))
        {
            score += 10;
        }
        
        // Prefer non-forced tracks
        if (!subtitle.IsForced)
        {
            score += 5;
        }
        
        // Default is a weak positive
        if (subtitle.IsDefault)
        {
            score += 3;
        }
        
        return score;
    }

    /// <summary>
    /// Finds the best track, allowing Signs/Songs but penalizing them.
    /// Used when the user has disabled "Skip Signs/Songs".
    /// </summary>
    private static EmbeddedSubtitle? FindBestTrackIncludingSignsSongs(List<EmbeddedSubtitle> candidates, string sourceLanguage)
    {
        EmbeddedSubtitle? best = null;
        var bestScore = int.MinValue;
        
        foreach (var subtitle in candidates)
        {
            var score = ScoreSubtitleCandidate(subtitle, sourceLanguage);
            
            if (score > bestScore ||
                (score == bestScore && best != null && subtitle.StreamIndex < best.StreamIndex))
            {
                bestScore = score;
                best = subtitle;
            }
        }
        
        return best;
    }

    /// <summary>
    /// Scores an embedded subtitle candidate based on language, title, and flags.
    /// Penalizes Signs/Songs but allows them to be selected if they are the best option.
    /// </summary>
    private static int ScoreSubtitleCandidate(EmbeddedSubtitle subtitle, string sourceLanguage)
    {
        var score = 0;

        if (LanguageMatches(subtitle.Language, sourceLanguage))
        {
            score += 50;
        }

        // Titles that usually indicate full dialogue tracks
        var title = subtitle.Title?.ToLowerInvariant() ?? string.Empty;
        if (title.Contains("full"))
        {
            score += 25;
        }

        if (title.Contains("dialog") || title.Contains("dialogue"))
        {
            score += 20;
        }

        if (title.Contains("sub") || title.Contains("subtitle"))
        {
            score += 10;
        }

        // Titles that typically indicate signs/songs/karaoke-only tracks
        if (title.Contains("sign") || title.Contains("song") || title.Contains("karaoke"))
        {
            score -= 40;
        }

        // Prefer non-forced tracks for full dialogue; forced tracks are often partial or effect-only.
        if (subtitle.IsForced)
        {
            score -= 10;
        }
        else
        {
            score += 5;
        }

        // Being the default stream is a weak positive signal (unless heavily penalized by title heuristics).
        if (subtitle.IsDefault)
        {
            score += 5;
        }

        return score;
    }

    /// <summary>
    /// Determines whether an embedded subtitle language matches the configured source language.
    /// Handles common 2-letter vs 3-letter ISO code differences (e.g. "en" vs "eng").
    /// </summary>
    private static bool LanguageMatches(string? subtitleLanguage, string? sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(subtitleLanguage) || string.IsNullOrWhiteSpace(sourceLanguage))
        {
            return false;
        }

        var sub = subtitleLanguage.Trim().ToLowerInvariant();
        var src = sourceLanguage.Trim().ToLowerInvariant();

        if (sub == src)
        {
            return true;
        }

        // Treat 2-letter and 3-letter variants that share a prefix as equivalent
        if (sub.Length == 3 && src.Length == 2 && sub.StartsWith(src, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (sub.Length == 2 && src.Length == 3 && src.StartsWith(sub, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
