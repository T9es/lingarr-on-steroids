using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
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
    private readonly IBatchFallbackService _batchFallbackService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ITranslationCancellationService _cancellationService;
    private readonly IMediaStateService _mediaStateService;
    private readonly IDeferredRepairService _deferredRepairService;

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
        IBatchFallbackService batchFallbackService,
        ISubtitleExtractionService extractionService,
        ITranslationCancellationService cancellationService,
        IMediaStateService mediaStateService,
        IDeferredRepairService deferredRepairService)
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
        _batchFallbackService = batchFallbackService;
        _extractionService = extractionService;
        _cancellationService = cancellationService;
        _mediaStateService = mediaStateService;
        _deferredRepairService = deferredRepairService;
    }

    /// <summary>
    /// Executes a translation job. Called by TranslationWorkerService.
    /// Concurrency is managed by the worker service, not internally.
    /// </summary>
    public Task ExecuteAsync(int translationRequestId, CancellationToken cancellationToken)
        => ExecuteCore(translationRequestId, cancellationToken);

    private async Task ExecuteCore(
        int translationRequestId,
        CancellationToken cancellationToken)
    {
        // Fetch the fresh request from the database
        // This ensures we have the latest state and avoids serialization issues with Hangfire
        var translationRequest = await _dbContext.TranslationRequests
            .FirstOrDefaultAsync(r => r.Id == translationRequestId, cancellationToken);

        if (translationRequest == null)
        {
            _logger.LogWarning("Translation request {RequestId} not found - it may have been deleted. Aborting job.", translationRequestId);
            return;
        }

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

        // Note: JobContextFilter may not be available without Hangfire context
        // This is fine - we can skip job name/id logging for worker-invoked jobs

        // Register this job for cooperative cancellation and create a linked token
        var jobCancellationToken = _cancellationService.RegisterJob(translationRequest.Id);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, jobCancellationToken);
        var effectiveCancellationToken = linkedCts.Token;

        string? temporaryFilePath = null;
        try
        {
            effectiveCancellationToken.ThrowIfCancellationRequested();

            var request = await _translationRequestService.UpdateTranslationRequest(
                translationRequest,
                TranslationStatus.InProgress,
                null); // No Hangfire job ID

            var subtitlePathForLog = translationRequest.SubtitleToTranslate ?? "Unknown";
            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                subtitlePathForLog);
            AddRequestLog("Information", $"TranslateJob started for subtitle: {subtitlePathForLog}");;

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
                SettingKeys.Translation.AiContextAfter,
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.RemoveLanguageTag,
                SettingKeys.Translation.UseSubtitleTagging,
                SettingKeys.Translation.SubtitleTag,
                SettingKeys.Translation.SubtitleTagShort,
                SettingKeys.Translation.EnableBatchFallback,
                SettingKeys.Translation.MaxBatchSplitAttempts,
                SettingKeys.Translation.BatchRetryMode,
                SettingKeys.Translation.RepairContextRadius,
                SettingKeys.Translation.RepairMaxRetries,
                SettingKeys.Translation.StripAssDrawingCommands,
                SettingKeys.Translation.CleanSourceAssDrawings,
                SettingKeys.Translation.BatchContextEnabled,
                SettingKeys.Translation.BatchContextBefore,
                SettingKeys.Translation.BatchContextAfter
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
                if (request.MediaId.HasValue)
                {
                    subtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(request.MediaId.Value, request.MediaType, request.SourceLanguage);
                }
                else
                {
                    _logger.LogWarning("Cannot extract embedded subtitle: MediaId is null");
                    subtitlePath = null;
                }
                
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
                _batchFallbackService,
                _deferredRepairService);
            List<SubtitleItem> subtitles;
            var attempt = 0;
            const int maxAttempts = 3;
            var excludedPaths = new List<string>();

            while (true)
            {
                subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
                AddRequestLog("Information", $"Loaded subtitle file with {subtitles.Count} entries for translation");

                if (subtitles.Count > 0)
                {
                    break;
                }

                attempt++;
                if (attempt > maxAttempts || !request.MediaId.HasValue)
                {
                    _logger.LogError("Translation failed: Subtitle file is empty ({Entries} entries) after {Attempt} attempts", subtitles.Count, attempt);
                    throw new InvalidOperationException($"Translation failed: Subtitle file is empty ({subtitles.Count} entries). " +
                                                        (request.MediaId.HasValue 
                                                            ? "Exhausted fallback attempts." 
                                                            : "No MediaId available for fallback."));
                }

                _logger.LogWarning("Loaded 0 entries from {Path}. Attempting fallback extraction (Attempt {Attempt}/{Max})...", 
                    request.SubtitleToTranslate, attempt, maxAttempts);
                AddRequestLog("Warning", $"Loaded 0 entries. Attempting embedded subtitle fallback (Attempt {attempt}/{maxAttempts})...");

                if (!string.IsNullOrEmpty(request.SubtitleToTranslate))
                {
                    excludedPaths.Add(request.SubtitleToTranslate);
                }

                var newSubtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(
                    request.MediaId.Value,
                    request.MediaType,
                    request.SourceLanguage,
                    excludedPaths);

                if (string.IsNullOrEmpty(newSubtitlePath))
                {
                    _logger.LogError("Fallback failed: No alternative embedded subtitles found");
                    throw new InvalidOperationException("Translation failed: Subtitle file is empty and no alternative embedded subtitles found.");
                }

                // Update request to point to new file
                request.SubtitleToTranslate = newSubtitlePath;
                temporaryFilePath = newSubtitlePath; // Mark for deletion
                
                _logger.LogInformation("Fallback successful, switching to: {Path}", newSubtitlePath);
                AddRequestLog("Information", $"Fallback successful, switching to: {newSubtitlePath}");
            }
            
            // Parse batch retry mode settings
            // "deferred" = collect failures and repair at end (default)
            // "immediate" = use immediate chunk splitting on failure (legacy)
            var batchRetryMode = settings.TryGetValue(SettingKeys.Translation.BatchRetryMode, out var modeVal) 
                ? modeVal ?? "deferred" 
                : "deferred";
            var maxBatchSplitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var splitAttempts)
                ? splitAttempts
                : 3;
            var repairContextRadius = int.TryParse(
                settings.TryGetValue(SettingKeys.Translation.RepairContextRadius, out var radiusVal) ? radiusVal : null, out var radius)
                ? radius
                : 10;
            var repairMaxRetries = int.TryParse(
                settings.TryGetValue(SettingKeys.Translation.RepairMaxRetries, out var retriesVal) ? retriesVal : null, out var retries)
                ? retries
                : 1;
            
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
                    "[{FileId}] Starting batch translation: {SubtitleCount} subtitles, {TotalBatches} batch(es) of {BatchSize}, retryMode: {RetryMode}",
                    fileIdentifier, subtitles.Count, totalBatches, effectiveBatchSize, batchRetryMode);

                AddRequestLog(
                    "Information",
                    $"[{fileIdentifier}] Starting batch translation: subtitles={subtitles.Count}, totalBatches={totalBatches}, batchSize={effectiveBatchSize}, retryMode={batchRetryMode}");

                // Parse batch context settings
                var batchContextEnabled = settings.TryGetValue(SettingKeys.Translation.BatchContextEnabled, out var ctxEnabled) && ctxEnabled == "true";
                var batchContextBefore = int.TryParse(
                    settings.TryGetValue(SettingKeys.Translation.BatchContextBefore, out var ctxBefore) ? ctxBefore : null, out var beforeLines)
                    ? beforeLines
                    : 3;
                var batchContextAfter = int.TryParse(
                    settings.TryGetValue(SettingKeys.Translation.BatchContextAfter, out var ctxAfter) ? ctxAfter : null, out var afterLines)
                    ? afterLines
                    : 3;

                translatedSubtitles = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    maxSize,
                    batchRetryMode,
                    maxBatchSplitAttempts,
                    repairContextRadius,
                    repairMaxRetries,
                    batchContextEnabled,
                    batchContextBefore,
                    batchContextAfter,
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

            var subtitleTag = settings[SettingKeys.Translation.UseSubtitleTagging] == "true"
                ? settings[SettingKeys.Translation.SubtitleTag]
                : null;
            var subtitleTagShort = settings[SettingKeys.Translation.UseSubtitleTagging] == "true"
                ? settings[SettingKeys.Translation.SubtitleTagShort]
                : null;

            await WriteSubtitles(request, translatedSubtitles, stripSubtitleFormatting, subtitleTag ?? "", subtitleTagShort ?? "", removeLanguageTag);
            AddRequestLog("Information", "Translation completed successfully and subtitle file was written");
            await HandleCompletion(request, effectiveCancellationToken);
        }
        catch (TaskCanceledException)
        {
            await HandleCancellation(translationRequest);
        }
        catch (OperationCanceledException)
        {
            // Also catch OperationCanceledException for cooperative cancellation
            await HandleCancellation(translationRequest);
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
                // Retry logic for status update - prevents jobs getting stuck in InProgress
                // when database is temporarily unavailable
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        translationRequest = await _translationRequestService.UpdateTranslationRequest(
                            translationRequest,
                            TranslationStatus.Failed,
                            null);
                        break; // Success, exit retry loop
                    }
                    catch (Exception retryEx) when (attempt < 2)
                    {
                        _logger.LogWarning(retryEx, 
                            "Attempt {Attempt}/3 failed to update job status. Retrying in {Delay}s...", 
                            attempt + 1, attempt + 1);
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1)); // 1s, 2s backoff
                    }
                }

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
        string subtitleTagShort,
        bool removeLanguageTag)
    {
        try
        {
            var targetLanguage = removeLanguageTag ? "" : translationRequest.TargetLanguage;

            var paths = _subtitleService.CreateFallbackPaths(
                translationRequest.SubtitleToTranslate,
                targetLanguage,
                subtitleTag,
                subtitleTagShort);
            
            Exception? lastException = null;
            bool success = false;
            string usedPath = "";

            foreach (var path in paths)
            {
                try
                {
                    await _subtitleService.WriteSubtitles(path, translatedSubtitles, stripSubtitleFormatting);
                    success = true;
                    usedPath = path;
                    break;
                }
                catch (PathTooLongException ex)
                {
                    _logger.LogWarning("Path too long: {Path}. Trying fallback...", path);
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write subtitle to {Path}. Trying fallback...", path);
                    lastException = ex;
                }
            }

            if (!success)
            {
                if (lastException != null)
                {
                    throw lastException;
                }
                throw new Exception("Failed to write subtitle to any of the fallback paths.");
            }

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                usedPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private async Task HandleCompletion(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
	        translationRequest.CompletedAt = DateTime.UtcNow;
	        translationRequest.Status = TranslationStatus.Completed;
	        translationRequest.IsActive = null;
	        await _dbContext.SaveChangesAsync(cancellationToken);
	        await _translationRequestService.UpdateActiveCount();
	        await _progressService.Emit(translationRequest, 100);
	        
	        // Update translation state to reflect completion
	        if (translationRequest.MediaId.HasValue)
	        {
	            try
	            {
	                if (translationRequest.MediaType == MediaType.Movie)
	                {
	                    var movie = await _dbContext.Movies.FindAsync(translationRequest.MediaId.Value);
	                    if (movie != null)
	                    {
	                        await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
	                    }
	                }
	                else
	                {
	                    var episode = await _dbContext.Episodes
	                        .Include(e => e.Season)
	                        .ThenInclude(s => s.Show)
	                        .FirstOrDefaultAsync(e => e.Id == translationRequest.MediaId.Value, cancellationToken);
	                    if (episode != null)
	                    {
	                        await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
	                    }
	                }
	            }
	            catch (Exception ex)
	            {
	                _logger.LogWarning(ex, "Failed to update translation state after completion");
	            }
	        }
    }

    private async Task HandleCancellation(TranslationRequest request)
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
	            translationRequest.IsActive = null;
	
	            await _dbContext.SaveChangesAsync();
	            await _translationRequestService.ClearMediaHash(translationRequest);
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
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
    


}
