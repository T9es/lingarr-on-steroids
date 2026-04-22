using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Extensions;
using Lingarr.Server.Services.Subtitle;
using System.Text.Json;
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
    private readonly ICustomMediaStateService _customMediaStateService;
    private readonly IDeferredRepairService _deferredRepairService;
    private readonly IDashboardService _dashboardService;
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly IUploadWorkspaceService _uploadWorkspaceService;

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
        ICustomMediaStateService customMediaStateService,
        IDeferredRepairService deferredRepairService,
        IDashboardService dashboardService,
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        IUploadWorkspaceService uploadWorkspaceService)
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
        _customMediaStateService = customMediaStateService;
        _deferredRepairService = deferredRepairService;
        _dashboardService = dashboardService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _uploadWorkspaceService = uploadWorkspaceService;
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

            // Set when translation actually started
            request.StartedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(effectiveCancellationToken);

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
                SettingKeys.Translation.SubtitleOutputMode,
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

            var subtitlePath = request.SubtitleToTranslate;
            if (request.WorkloadKind == TranslationWorkloadKind.Upload)
            {
                subtitlePath = await _uploadWorkspaceService.PrepareSubtitleForRequestAsync(
                    request,
                    effectiveCancellationToken);

                if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
                {
                    var errorMessage = $"Upload source subtitle could not be prepared for request {request.Id}.";
                    _logger.LogError(errorMessage);
                    AddRequestLog("Error", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                request.SubtitleToTranslate = subtitlePath;
            }
            // AUTO-EXTRACTION: If subtitle file doesn't exist, check for embedded subtitles
            else if (string.IsNullOrEmpty(subtitlePath) || !File.Exists(subtitlePath))
            {
                _logger.LogInformation("Subtitle file not found, checking for embedded subtitles...");
                AddRequestLog("Warning", "Subtitle file not found on disk, attempting embedded subtitle extraction");

                if (request.WorkloadKind == TranslationWorkloadKind.CustomSource && request.CustomMediaItemId.HasValue)
                {
                    subtitlePath = await TryExtractCustomSourceSubtitleAsync(request, effectiveCancellationToken);
                    if (!string.IsNullOrWhiteSpace(subtitlePath))
                    {
                        temporaryFilePath = subtitlePath;
                    }
                }
                else if (request.MediaId.HasValue)
                {
                    var streamSelectionKey = $"subtitle_stream_selection_{request.MediaId.Value}_{request.MediaType}";
                    var preferredStreamIndexSetting = await _settings.GetSetting(streamSelectionKey);
                    int? preferredStreamIndex = null;
                    if (!string.IsNullOrEmpty(preferredStreamIndexSetting) && int.TryParse(preferredStreamIndexSetting, out var parsedIndex))
                    {
                        preferredStreamIndex = parsedIndex;
                        _logger.LogInformation("Using preferred stream index {StreamIndex} from manual selection", preferredStreamIndex);
                        AddRequestLog("Information", $"Using preferred stream index {preferredStreamIndex} from manual selection");
                        await _settings.SetSetting(streamSelectionKey, "");
                    }

                    var preExistingExtractedPaths = await GetPreExistingExtractedSubtitlePathsAsync(
                        request.MediaId.Value,
                        request.MediaType,
                        request.SourceLanguage);

                    subtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(
                        request.MediaId.Value,
                        request.MediaType,
                        request.SourceLanguage,
                        null,
                        preferredStreamIndex);

                    if (!string.IsNullOrEmpty(subtitlePath))
                    {
                        var wasPreExistingFile = IsPreExistingExtractionPath(subtitlePath, preExistingExtractedPaths);
                        if (!wasPreExistingFile)
                        {
                            temporaryFilePath = subtitlePath;
                            _logger.LogDebug("Marked extracted subtitle as temporary (auto-extracted, will be cleaned up): {Path}", subtitlePath);
                        }
                        else
                        {
                            _logger.LogDebug("Subtitle file existed before extraction (user-provided), preserving file: {Path}", subtitlePath);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Cannot extract embedded subtitle because the request has no workload identity");
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

                request.SubtitleToTranslate = subtitlePath;
                _logger.LogInformation("Using extracted embedded subtitle: {Path}", subtitlePath);
                AddRequestLog("Information", $"Using extracted embedded subtitle: {subtitlePath}");
            }

            var subtitleOutputMode = !string.IsNullOrWhiteSpace(request.SubtitleOutputMode)
                ? SubtitleOutputModeHelper.Parse(request.SubtitleOutputMode)
                : SubtitleOutputModeHelper.Parse(settings.GetValueOrDefault(SettingKeys.Translation.SubtitleOutputMode));
            var actualSourceFormat = string.Empty;
            IReadOnlyList<string> requiredOutputFormats = [];
            var writesPreservedAssOutput = false;
            var preserveAssFormatting = false;
            var translationStripSubtitleFormatting = stripSubtitleFormatting;

            async Task RefreshRequestOutputMetadataAsync()
            {
                actualSourceFormat = SubtitleOutputModeHelper.NormalizeFormat(Path.GetExtension(request.SubtitleToTranslate));
                requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(actualSourceFormat, subtitleOutputMode);
                var sourceIsAssFormat = SubtitleOutputModeHelper.IsAssFormat(actualSourceFormat);
                writesPreservedAssOutput = sourceIsAssFormat && requiredOutputFormats.Any(SubtitleOutputModeHelper.IsAssFormat);
                preserveAssFormatting = writesPreservedAssOutput;
                translationStripSubtitleFormatting = stripSubtitleFormatting && !writesPreservedAssOutput;

                request.SourceSubtitleFormat = actualSourceFormat;
                request.SubtitleOutputMode = subtitleOutputMode.ToSettingValue();
                request.RequiredOutputFormats = SubtitleOutputModeHelper.SerializeFormats(requiredOutputFormats);
                await _dbContext.SaveChangesAsync(effectiveCancellationToken);
            }

            await RefreshRequestOutputMetadataAsync();

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
                    StripSubtitleFormatting = translationStripSubtitleFormatting
                };

                if (string.IsNullOrEmpty(request.SubtitleToTranslate) || !_subtitleService.ValidateSubtitle(request.SubtitleToTranslate, validationOptions))
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
            var excludedStreamIndices = new List<int>();

            // Generate file identifier early for logging
            var fileIdentifier = GenerateFileIdentifier(request.SubtitleToTranslate!);

            EmbeddedSubtitle? selectedSubtitle = null;
            while (true)
            {
                subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate!);
                AddRequestLog("Information", $"Loaded subtitle file with {subtitles.Count} entries for translation");

                // Capture subtitle tracking metadata
                if (subtitles.Count > 0)
                {
                    request.SourceSubtitleEntryCount = subtitles.Count;
                    selectedSubtitle = await GetEmbeddedSubtitleMetadata(request);
                    var sourceIsEmbedded = ShouldUseEmbeddedSourceSubtitle(
                        request.SubtitleToTranslate,
                        selectedSubtitle);

                    if (sourceIsEmbedded && selectedSubtitle != null)
                    {
                        request.SelectedStreamTitle = selectedSubtitle.Title;
                        request.IsForcedSubtitle = selectedSubtitle.IsForced;
                        request.SourceSubtitleType = DetermineSubtitleType(selectedSubtitle);
                        _logger.LogInformation(
                            "[{FileId}] Captured subtitle metadata: Type={Type}, Entries={Entries}, Title={Title}, Forced={Forced}",
                            fileIdentifier, request.SourceSubtitleType, request.SourceSubtitleEntryCount,
                            request.SelectedStreamTitle ?? "N/A", request.IsForcedSubtitle);
                    }
                    else
                    {
                        // For external subtitle files, try to determine type from filename
                        request.SourceSubtitleType = DetermineSubtitleTypeFromFilename(request.SubtitleToTranslate);
                        _logger.LogInformation(
                            "[{FileId}] External subtitle: Type={Type}, Entries={Entries}",
                            fileIdentifier, request.SourceSubtitleType, request.SourceSubtitleEntryCount);
                    }

                    var sourceSnapshot = sourceIsEmbedded && selectedSubtitle != null
                        ? _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(
                            selectedSubtitle,
                            request.SourceLanguage)
                        : _sourceSubtitleSnapshotService.CreateExternalSnapshot(
                            request.SubtitleToTranslate!,
                            request.SourceLanguage);

                    request.SourceSnapshotVersion = sourceSnapshot.Version;
                    request.SourceSnapshotType = sourceSnapshot.SourceType;
                    request.SourceSnapshotIdentity = sourceSnapshot.Identity;
                    request.SourceSnapshotFingerprint = sourceSnapshot.Fingerprint;
                    request.SourceSnapshotFileSizeBytes = sourceSnapshot.FileSizeBytes;
                    request.SourceSnapshotLastWriteUtc = sourceSnapshot.LastWriteUtc;
                    request.SourceSnapshotStreamIndex = sourceSnapshot.StreamIndex;
                    await _dbContext.SaveChangesAsync(effectiveCancellationToken);
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

                // If we have metadata about which stream was used, exclude it for fallback
                if (selectedSubtitle != null && !excludedStreamIndices.Contains(selectedSubtitle.StreamIndex))
                {
                    excludedStreamIndices.Add(selectedSubtitle.StreamIndex);
                    _logger.LogInformation("Excluding stream {StreamIndex} from fallback selection", selectedSubtitle.StreamIndex);
                }

                var fallbackPreExistingExtractedPaths = await GetPreExistingExtractedSubtitlePathsAsync(
                    request.MediaId.Value,
                    request.MediaType,
                    request.SourceLanguage);
                
                var newSubtitlePath = await _extractionService.TryExtractEmbeddedSubtitle(
                    request.MediaId.Value,
                    request.MediaType,
                    request.SourceLanguage,
                    excludedStreamIndices,
                    null); // Don't use preferred stream for fallback - we want a different stream

                if (string.IsNullOrEmpty(newSubtitlePath))
                {
                    _logger.LogError("Fallback failed: No alternative embedded subtitles found");
                    throw new InvalidOperationException("Translation failed: Subtitle file is empty and no alternative embedded subtitles found.");
                }

                // Update request to point to new file
                request.SubtitleToTranslate = newSubtitlePath;
                await RefreshRequestOutputMetadataAsync();

                var wasPreExistingFallbackFile = IsPreExistingExtractionPath(
                    newSubtitlePath,
                    fallbackPreExistingExtractedPaths);

                if (!wasPreExistingFallbackFile)
                {
                    // Mark for cleanup since this was auto-extracted during fallback.
                    temporaryFilePath = newSubtitlePath;
                    _logger.LogDebug("Marked fallback extracted subtitle as temporary: {Path}", newSubtitlePath);
                }
                else
                {
                    _logger.LogDebug(
                        "Fallback returned subtitle file that existed before extraction, preserving file: {Path}",
                        newSubtitlePath);
                }
                
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
            
            // Parse ASS drawing command filter settings
            var stripAssDrawingCommands = settings.TryGetValue(SettingKeys.Translation.StripAssDrawingCommands, out var stripAssVal) && stripAssVal == "true";
            var cleanSourceAssDrawings = settings.TryGetValue(SettingKeys.Translation.CleanSourceAssDrawings, out var cleanSourceVal) && cleanSourceVal == "true";
            
            // Filter out ASS drawing commands if enabled
            if (stripAssDrawingCommands && !writesPreservedAssOutput)
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
                    await CleanSourceSubtitleFile(request.SubtitleToTranslate!, stripSubtitleFormatting);
                    _logger.LogInformation("[{FileId}] Cleaned ASS drawing commands from source file", fileIdentifier);
                    AddRequestLog("Information",
                        $"[{fileIdentifier}] Cleaned ASS drawing commands from source subtitle file");
                }
            }
            
            List<SubtitleItem> translatedSubtitles;
            
            var useBatchSetting = settings.TryGetValue(SettingKeys.Translation.UseBatchTranslation, out var useBatchVal) 
                                  ? useBatchVal 
                                  : "false";
            var useBatchTranslation = string.Equals(useBatchSetting, "true", StringComparison.OrdinalIgnoreCase);
            var isBatchService = translationService is IBatchTranslationService;

            if (useBatchTranslation && isBatchService)
            {
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize],
                    out var batchSize)
                    ? batchSize
                    : 10000;
                
                var effectiveBatchSize = maxSize <= 0 ? subtitles.Count : maxSize;

                _logger.LogInformation(
                    "[{FileId}] Starting batch translation prep: source subtitles={SubtitleCount}, configured batchSize={BatchSize}, retryMode={RetryMode}. Optimized provider batch count is logged after structure analysis.",
                    fileIdentifier, subtitles.Count, effectiveBatchSize, batchRetryMode);

                AddRequestLog(
                    "Information",
                    $"[{fileIdentifier}] Starting batch translation prep: sourceSubtitles={subtitles.Count}, configuredBatchSize={effectiveBatchSize}, retryMode={batchRetryMode}");

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
                    translationStripSubtitleFormatting,
                    preserveAssFormatting,
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
                    "[{FileId}] Batch translation skipped. UseBatchTranslation: {UseBatch} (Value: '{Value}'), Service: {ServiceType}, IsBatchService: {IsBatchService}",
                    fileIdentifier, useBatchTranslation, useBatchSetting ?? "null", translationService.GetType().Name, isBatchService);

                _logger.LogInformation(
                    "[{FileId}] Starting individual translation: {SubtitleCount} subtitles, context (before: {ContextBefore}, after: {ContextAfter})",
                    fileIdentifier, subtitles.Count, contextBefore, contextAfter);
                AddRequestLog(
                    "Information",
                    $"[{fileIdentifier}] Starting individual translation: subtitles={subtitles.Count}, contextBefore={contextBefore}, contextAfter={contextAfter}");

                translatedSubtitles = await translator.TranslateSubtitles(
                    subtitles,
                    request,
                    translationStripSubtitleFormatting,
                    contextBefore,
                    contextAfter,
                    preserveAssFormatting,
                    effectiveCancellationToken
                );
            }

            if (settings[SettingKeys.Translation.FixOverlappingSubtitles] == "true" && !writesPreservedAssOutput)
            {
                translatedSubtitles = _subtitleService.FixOverlappingSubtitles(translatedSubtitles);
            }

            if (addTranslatorInfo && !writesPreservedAssOutput)
            {
                _subtitleService.AddTranslatorInfo(serviceType, translatedSubtitles, translationService);
            }

            if (translationStripSubtitleFormatting && translatedSubtitles.Count > 0)
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

            var writtenOutput = await WriteSubtitles(
                request,
                translatedSubtitles,
                stripSubtitleFormatting,
                subtitleTag ?? "",
                subtitleTagShort ?? "",
                removeLanguageTag,
                writesPreservedAssOutput,
                effectiveCancellationToken);
            request.TranslatedSubtitle = writtenOutput.PrimaryPath;
            request.GeneratedOutputFormats = writtenOutput.GeneratedFormats;
            request.GeneratedSubtitlePaths = JsonSerializer.Serialize(writtenOutput.OutputPaths);
            AddRequestLog(
                "Information",
                $"Translation completed successfully and subtitle file was written to: {writtenOutput.PrimaryPath}");
            await HandleCompletion(request, writtenOutput.OutputPaths, effectiveCancellationToken);
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
                // Calculate exponential backoff for next retry
                // 1st failure: 1 hour, 2nd: 4 hours, 3rd: 12 hours, 4th+: 24 hours
                var retryDelay = translationRequest.RetryCount switch
                {
                    0 => TimeSpan.FromHours(1),
                    1 => TimeSpan.FromHours(4),
                    2 => TimeSpan.FromHours(12),
                    _ => TimeSpan.FromHours(24)
                };
                
                var now = DateTime.UtcNow;
                var nextRetryAt = now.Add(retryDelay);
                
                // Retry logic for status update - prevents jobs getting stuck in InProgress
                // when database is temporarily unavailable
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        // Update retry tracking fields before status update
                        translationRequest.RetryCount++;
                        translationRequest.FailedAt = now;
                        translationRequest.NextRetryAt = nextRetryAt;
                        
                        translationRequest = await _translationRequestService.UpdateTranslationRequest(
                            translationRequest,
                            TranslationStatus.Failed,
                            null);

                        // Update translation state to reflect failure
                        try
                        {
                            await RefreshTranslationStateAsync(translationRequest, effectiveCancellationToken);
                        }
                        catch (Exception stateEx)
                        {
                            _logger.LogWarning(stateEx, "Failed to update translation state after failure");
                        }
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
                var failureSummary = TranslationFailureClassifier.GetFailureSummary(ex);
                var failureMessage = $"Translation failed: {failureSummary}";
                var failureDetails = TranslationFailureClassifier.IsProviderUnavailable(ex)
                    ? $"Root cause: translation provider unavailable.{Environment.NewLine}Summary: {failureSummary}{Environment.NewLine}{Environment.NewLine}{ex}"
                    : ex.ToString();
                _logger.LogError(ex, "Translation failed for request {RequestId}", translationRequest.Id);

                if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
                {
                    try
                    {
                        await _uploadWorkspaceService.HandleRequestFailedAsync(
                            translationRequest,
                            failureMessage,
                            effectiveCancellationToken);
                    }
                    catch (Exception uploadHookEx)
                    {
                        _logger.LogWarning(
                            uploadHookEx,
                            "Failed to update upload workspace state for failed request {RequestId}",
                            translationRequest.Id);
                    }
                }
                
                // Log to dashboard
                await _dashboardService.LogError(
                    "TranslationJob",
                    failureMessage,
                    $"Request ID: {translationRequest.Id}\nMedia ID: {translationRequest.MediaId}",
                    failureDetails
                );
                
                _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
                {
                    TranslationRequestId = translationRequest.Id,
                    Level = "Error",
                    Message = failureMessage,
                    Details = failureDetails
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

            await CleanupTemporaryExtractedSubtitleAsync(translationRequest, temporaryFilePath);
        }
    }

    private async Task<WrittenSubtitleOutput> WriteSubtitles(TranslationRequest translationRequest,
        List<SubtitleItem> translatedSubtitles,
        bool stripSubtitleFormatting,
        string subtitleTag,
        string subtitleTagShort,
        bool removeLanguageTag,
        bool writesPreservedAssOutput,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetLanguage = removeLanguageTag ? "" : translationRequest.TargetLanguage;
            var requiredOutputFormats = SubtitleOutputModeHelper.DeserializeFormats(translationRequest.RequiredOutputFormats);
            if (requiredOutputFormats.Count == 0)
            {
                requiredOutputFormats = SubtitleOutputModeHelper.GetRequiredOutputFormats(
                    translationRequest.SourceSubtitleFormat,
                    SubtitleOutputModeHelper.Parse(translationRequest.SubtitleOutputMode));
            }

            var writtenOutputs = new List<(string Format, string Path)>();

            foreach (var outputFormat in requiredOutputFormats)
            {
                var renderSubtitles = BuildOutputSubtitles(
                    translatedSubtitles,
                    outputFormat,
                    writesPreservedAssOutput);
                var outputStripFormatting = stripSubtitleFormatting
                                            && !(writesPreservedAssOutput
                                                && SubtitleOutputModeHelper.IsAssFormat(outputFormat));
                var paths = translationRequest.WorkloadKind == TranslationWorkloadKind.Upload
                    ? await _uploadWorkspaceService.GetOutputPathsAsync(
                        translationRequest,
                        targetLanguage,
                        subtitleTag,
                        subtitleTagShort,
                        outputFormat,
                        cancellationToken)
                    : _subtitleService.CreateFallbackPaths(
                        translationRequest.SubtitleToTranslate!,
                        targetLanguage,
                        subtitleTag,
                        subtitleTagShort,
                        outputFormat);

                Exception? lastException = null;
                bool success = false;
                string usedPath = "";

                foreach (var path in paths)
                {
                    try
                    {
                        await _subtitleService.WriteSubtitles(path, renderSubtitles, outputStripFormatting);
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

                    throw new Exception($"Failed to write subtitle to any fallback path for format {outputFormat}.");
                }

                writtenOutputs.Add((outputFormat, usedPath));
            }

            var primaryPath = writtenOutputs
                .OrderByDescending(output =>
                    string.Equals(
                        SubtitleOutputModeHelper.NormalizeFormat(output.Format),
                        SubtitleOutputModeHelper.NormalizeFormat(translationRequest.SourceSubtitleFormat),
                        StringComparison.OrdinalIgnoreCase))
                .Select(output => output.Path)
                .First();

            var generatedFormats = SubtitleOutputModeHelper.SerializeFormats(writtenOutputs.Select(output => output.Format));

            _logger.LogInformation(
                "TranslateJob completed and created subtitle outputs: |Green|{SubtitleOutputs}|/Green|",
                string.Join(", ", writtenOutputs.Select(output => output.Path)));
            return new WrittenSubtitleOutput(
                primaryPath,
                generatedFormats,
                writtenOutputs.Select(output => output.Path).ToList());
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private static List<SubtitleItem> BuildOutputSubtitles(
        List<SubtitleItem> translatedSubtitles,
        string outputFormat,
        bool writesPreservedAssOutput)
    {
        if (writesPreservedAssOutput && SubtitleOutputModeHelper.IsAssFormat(outputFormat))
        {
            return translatedSubtitles;
        }

        var renderedSubtitles = new List<SubtitleItem>(translatedSubtitles.Count);

        foreach (var subtitle in translatedSubtitles)
        {
            var translatedText = subtitle.TranslatedLines.Count > 0
                ? string.Join("\\N", subtitle.TranslatedLines)
                : string.Join("\\N", subtitle.Lines);
            var plainTextLines = ConvertToPlainTextLines(translatedText);

            if (writesPreservedAssOutput &&
                plainTextLines.Count == 0 &&
                SubtitleFormatterService.IsMeaningless(string.Join(" ", subtitle.PlaintextLines)))
            {
                continue;
            }

            renderedSubtitles.Add(new SubtitleItem
            {
                Position = subtitle.Position,
                StartTime = subtitle.StartTime,
                EndTime = subtitle.EndTime,
                Lines = [.. subtitle.Lines],
                PlaintextLines = [.. subtitle.PlaintextLines],
                TranslatedLines = plainTextLines,
                SsaDialogue = subtitle.SsaDialogue,
                SsaFormat = subtitle.SsaFormat
            });
        }

        return renderedSubtitles;
    }

    private static List<string> ConvertToPlainTextLines(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return [];
        }

        var normalized = SubtitleFormatterService.NormalizeLineBreaks(translatedText)
            .Replace("\\n", "\\N", StringComparison.Ordinal);
        var segments = normalized.Split("\\N", StringSplitOptions.None);
        var lines = new List<string>();

        foreach (var segment in segments)
        {
            var plainText = SubtitleFormatterService.RemoveMarkup(segment);
            if (string.IsNullOrWhiteSpace(plainText))
            {
                continue;
            }

            lines.AddRange(plainText.SplitIntoLines(42));
        }

        return lines;
    }

    private sealed record WrittenSubtitleOutput(
        string PrimaryPath,
        string GeneratedFormats,
        IReadOnlyCollection<string> OutputPaths);

    private async Task HandleCompletion(
        TranslationRequest translationRequest,
        IReadOnlyCollection<string> outputPaths,
        CancellationToken cancellationToken)
    {
        translationRequest.CompletedAt = DateTime.UtcNow;
        translationRequest.Status = TranslationStatus.Completed;
        translationRequest.IsActive = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
        {
            await _uploadWorkspaceService.HandleRequestCompletedAsync(
                translationRequest,
                outputPaths,
                cancellationToken);
        }

        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100);

        try
        {
            await RefreshTranslationStateAsync(translationRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state after completion");
        }
    }

    private async Task HandleCancellation(TranslationRequest request)
    {
        var cleanupToken = CancellationToken.None;

        _logger.LogInformation("Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
            request.SubtitleToTranslate);
        var translationRequest =
            await _dbContext.TranslationRequests.FirstOrDefaultAsync(translationRequest =>
                translationRequest.Id == request.Id, cleanupToken);

        if (translationRequest != null)
        {
            translationRequest.CompletedAt = DateTime.UtcNow;
            translationRequest.Status = TranslationStatus.Cancelled;
            translationRequest.IsActive = null;

            await _dbContext.SaveChangesAsync(cleanupToken);
            if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
            {
                await _uploadWorkspaceService.HandleRequestCancelledAsync(translationRequest, cleanupToken);
            }

            await _translationRequestService.ClearMediaHash(translationRequest);
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            await RefreshTranslationStateAsync(translationRequest, cleanupToken);
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
    internal async Task CleanSourceSubtitleFile(string subtitlePath, bool stripSubtitleFormatting)
    {
        try
        {
            var wasLingarrExtracted = SubtitleExtractionService.IsLingarrExtracted(subtitlePath);

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

                if (wasLingarrExtracted)
                {
                    await RestoreExtractionMarkerAsync(subtitlePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean source subtitle file: {Path}", subtitlePath);
            // Don't throw - this is a non-critical operation
        }
    }

    internal async Task CleanupTemporaryExtractedSubtitleAsync(
        TranslationRequest translationRequest,
        string? temporaryFilePath)
    {
        if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(temporaryFilePath))
        {
            return;
        }

        if (File.Exists(temporaryFilePath))
        {
            if (SubtitleExtractionService.IsLingarrExtracted(temporaryFilePath))
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
            else
            {
                _logger.LogWarning("Not deleting {Path} - no Lingarr marker (user file)", temporaryFilePath);
                return;
            }
        }

        if (File.Exists(temporaryFilePath))
        {
            return;
        }

        if (translationRequest.WorkloadKind == TranslationWorkloadKind.CustomSource)
        {
            return;
        }

        if (!translationRequest.MediaId.HasValue)
        {
            return;
        }

        try
        {
            await _extractionService.ClearExtractionMetadataAsync(
                translationRequest.MediaId.Value,
                translationRequest.MediaType,
                temporaryFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear extraction metadata for temporary subtitle: {Path}", temporaryFilePath);
        }
    }

    private static async Task RestoreExtractionMarkerAsync(string subtitlePath)
    {
        if (!File.Exists(subtitlePath) || SubtitleExtractionService.IsLingarrExtracted(subtitlePath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(subtitlePath);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{SubtitleExtractionService.ExtractionMarkerPrefix} Preserved=true");
        builder.AppendLine();
        builder.Append(content);

        await File.WriteAllTextAsync(subtitlePath, builder.ToString());
    }

    private async Task RefreshTranslationStateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.WorkloadKind == TranslationWorkloadKind.CustomSource)
        {
            if (!request.CustomMediaItemId.HasValue)
            {
                return;
            }

            var customItem = await _dbContext.CustomMediaItems
                .FirstOrDefaultAsync(item => item.Id == request.CustomMediaItemId.Value, cancellationToken);
            if (customItem != null)
            {
                await _customMediaStateService.UpdateStateAsync(customItem);
            }

            return;
        }

        if (request.WorkloadKind == TranslationWorkloadKind.Upload)
        {
            return;
        }

        if (!request.MediaId.HasValue)
        {
            return;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies.FindAsync([request.MediaId.Value], cancellationToken);
            if (movie != null)
            {
                await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
            }

            return;
        }

        var episode = await _dbContext.Episodes
            .Include(item => item.Season)
            .ThenInclude(item => item.Show)
            .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
        if (episode != null)
        {
            await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
        }
    }

    private async Task<string?> TryExtractCustomSourceSubtitleAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.CustomMediaItemId.HasValue)
        {
            return null;
        }

        var customItem = await _dbContext.CustomMediaItems
            .FirstOrDefaultAsync(item => item.Id == request.CustomMediaItemId.Value, cancellationToken);
        if (customItem == null)
        {
            return null;
        }

        var outputDirectory = Path.GetDirectoryName(customItem.Path);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        var embeddedSubtitles = await _extractionService.ProbeEmbeddedSubtitles(customItem.Path);
        var candidate = SubtitleLanguageHelper.FindBestMatch(
            embeddedSubtitles.Where(subtitle => subtitle.IsTextBased).ToList(),
            [request.SourceLanguage]);

        if (candidate.Subtitle == null)
        {
            return null;
        }

        return await _extractionService.ExtractSubtitle(
            customItem.Path,
            candidate.Subtitle.StreamIndex,
            outputDirectory,
            candidate.Subtitle.CodecName,
            candidate.Subtitle.Language);
    }

    internal static bool IsPreExistingExtractionPath(
        string subtitlePath,
        IReadOnlySet<string> preExistingExtractedPaths)
    {
        return !string.IsNullOrWhiteSpace(subtitlePath) && preExistingExtractedPaths.Contains(subtitlePath);
    }

    internal async Task<HashSet<string>> GetPreExistingExtractedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType,
        string sourceLanguage)
    {
        try
        {
            IQueryable<EmbeddedSubtitle> query = mediaType switch
            {
                MediaType.Movie => _dbContext.EmbeddedSubtitles.Where(es => es.MovieId == mediaId),
                MediaType.Episode => _dbContext.EmbeddedSubtitles.Where(es => es.EpisodeId == mediaId),
                _ => _dbContext.EmbeddedSubtitles.Where(_ => false)
            };

            var existingSubtitles = await query
                .AsNoTracking()
                .Where(es => es.IsExtracted && !string.IsNullOrWhiteSpace(es.ExtractedPath))
                .Select(es => new { es.Language, es.ExtractedPath })
                .ToListAsync();

            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subtitle in existingSubtitles)
            {
                if (string.IsNullOrWhiteSpace(subtitle.ExtractedPath))
                {
                    continue;
                }

                if (!SubtitleLanguageHelper.LanguageMatches(subtitle.Language, sourceLanguage))
                {
                    continue;
                }

                if (File.Exists(subtitle.ExtractedPath))
                {
                    existingPaths.Add(subtitle.ExtractedPath);
                }
            }

            return existingPaths;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking pre-existing extracted subtitle paths for media {MediaId} ({MediaType})",
                mediaId,
                mediaType);
            return [];
        }
    }

    internal static bool ShouldUseEmbeddedSourceSubtitle(string? subtitlePath, EmbeddedSubtitle? selectedSubtitle)
    {
        if (selectedSubtitle == null || string.IsNullOrWhiteSpace(subtitlePath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selectedSubtitle.ExtractedPath) &&
            string.Equals(selectedSubtitle.ExtractedPath, subtitlePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SubtitleExtractionService.IsLingarrExtracted(subtitlePath);
    }

    /// <summary>
    /// Gets the embedded subtitle metadata that matches the currently selected subtitle file.
    /// Used for tracking subtitle type and stream information for audit/debugging.
    /// </summary>
    private async Task<EmbeddedSubtitle?> GetEmbeddedSubtitleMetadata(TranslationRequest request)
    {
        if (!request.MediaId.HasValue || string.IsNullOrEmpty(request.SubtitleToTranslate))
            return null;

        try
        {
            // Get embedded subtitles for this media
            List<EmbeddedSubtitle>? embeddedSubtitles = null;
            if (request.MediaType == MediaType.Episode)
            {
                var episode = await _dbContext.Episodes
                    .AsNoTracking()
                    .Include(e => e.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(e => e.Id == request.MediaId.Value);
                embeddedSubtitles = episode?.EmbeddedSubtitles?.ToList();
            }
            else if (request.MediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies
                    .AsNoTracking()
                    .Include(m => m.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(m => m.Id == request.MediaId.Value);
                embeddedSubtitles = movie?.EmbeddedSubtitles?.ToList();
            }

            if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
                return null;

            // Find the subtitle that matches the extracted path
            // Match by filename pattern: {mediaFile}.{language}.srt or contains the language code
            var subtitlePath = request.SubtitleToTranslate;
            var subtitleFileName = Path.GetFileNameWithoutExtension(subtitlePath);

            // First try exact match on ExtractedPath
            var matched = embeddedSubtitles.FirstOrDefault(es =>
                !string.IsNullOrEmpty(es.ExtractedPath) &&
                es.ExtractedPath.Equals(subtitlePath, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
                return matched;

            // Try to match by language code in the filename
            foreach (var es in embeddedSubtitles.Where(es => !string.IsNullOrEmpty(es.Language)))
            {
                if (subtitleFileName.Contains($".{es.Language}", StringComparison.OrdinalIgnoreCase) ||
                    subtitleFileName.Contains($"_{es.Language}", StringComparison.OrdinalIgnoreCase))
                {
                    return es;
                }
            }

            // Return the first text-based subtitle if we can't determine exactly which one
            return embeddedSubtitles.FirstOrDefault(es => es.IsTextBased);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting embedded subtitle metadata for request {RequestId}", request.Id);
            return null;
        }
    }

    /// <summary>
    /// Determines the subtitle type based on embedded subtitle metadata.
    /// </summary>
    private static string DetermineSubtitleType(EmbeddedSubtitle subtitle)
    {
        // Check title for common indicators
        if (!string.IsNullOrEmpty(subtitle.Title))
        {
            var title = subtitle.Title.ToLowerInvariant();
            if (title.Contains("sdh") || title.Contains("hearing") || title.Contains("deaf"))
                return "SDH";
            if (title.Contains("forced") || title.Contains("force") || title.Contains("foreign"))
                return "Forced";
            if (title.Contains("full") || title.Contains("dialogue") || title.Contains("complete"))
                return "Full";
            if (title.Contains("sign") || title.Contains("song"))
                return "Signs/Songs";
        }

        // Check if forced flag is set
        if (subtitle.IsForced)
            return "Forced";

        // Default to Unknown - caller can check entry count to infer
        return "Unknown";
    }

    /// <summary>
    /// Determines the subtitle type from external subtitle filename.
    /// </summary>
    private static string DetermineSubtitleTypeFromFilename(string? subtitlePath)
    {
        if (string.IsNullOrEmpty(subtitlePath))
            return "Unknown";

        var fileName = Path.GetFileNameWithoutExtension(subtitlePath).ToLowerInvariant();

        if (fileName.Contains(".sdh") || fileName.Contains("_sdh") ||
            fileName.Contains(".hi") || fileName.Contains("_hi") ||
            fileName.Contains("hearing"))
            return "SDH";

        if (fileName.Contains(".forced") || fileName.Contains("_forced") ||
            fileName.Contains(".force") || fileName.Contains("_force") ||
            fileName.Contains(".foreign") || fileName.Contains("_foreign"))
            return "Forced";

        if (fileName.Contains(".sign") || fileName.Contains("_sign") ||
            fileName.Contains("song"))
            return "Signs/Songs";

        // Default to Full for external files
        return "Full";
    }
}
