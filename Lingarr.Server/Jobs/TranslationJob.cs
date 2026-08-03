using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Extensions;
using Lingarr.Server.Services.Subtitle;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Extensions;
using SubtitleValidationOptions = Lingarr.Server.Models.SubtitleValidationOptions;

namespace Lingarr.Server.Jobs;

public class TranslationJob
{
    private static readonly HashSet<string> MediaFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".wmv",
        ".flv",
        ".webm",
        ".m4v",
        ".ts",
        ".m2ts"
    };

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
    private readonly ISourceSubtitleResolver _sourceSubtitleResolver;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly IUploadWorkspaceService _uploadWorkspaceService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ITranslationCheckpointService? _translationCheckpointService;
    private readonly ISubtitleQualityValidatorService _subtitleQualityValidatorService;
    private readonly ITranslationDiagnosticsService _translationDiagnosticsService;
    private readonly ITranslationPromptContextAccessor _translationPromptContextAccessor;
    private readonly IMkvEmbeddingService _mkvEmbeddingService;
    private readonly ITranslationSiblingSequenceApprovalService? _siblingSequenceApprovalService;

    internal Func<Task>? BeforeFinalCompletionCommitAsync { get; set; }

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
        ISourceSubtitleResolver sourceSubtitleResolver,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        IUploadWorkspaceService uploadWorkspaceService,
        ITranslationCheckpointService? translationCheckpointService = null,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null,
ISubtitleQualityValidatorService? subtitleQualityValidatorService = null,
        ITranslationDiagnosticsService? translationDiagnosticsService = null,
        ITranslationPromptContextAccessor? translationPromptContextAccessor = null,
        IMkvEmbeddingService? mkvEmbeddingService = null,
        ITranslationSiblingSequenceApprovalService? siblingSequenceApprovalService = null)
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
        _sourceSubtitleResolver = sourceSubtitleResolver;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _uploadWorkspaceService = uploadWorkspaceService;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
        _translationCheckpointService = translationCheckpointService;
        _subtitleQualityValidatorService = subtitleQualityValidatorService ??
            new SubtitleQualityValidatorService(
                subtitleService,
                NullLogger<SubtitleQualityValidatorService>.Instance);
        _translationDiagnosticsService = translationDiagnosticsService ??
            new TranslationDiagnosticsService(
                dbContext,
                NullLogger<TranslationDiagnosticsService>.Instance);
_translationPromptContextAccessor = translationPromptContextAccessor ??
            new Services.Translation.TranslationPromptContextAccessor();
        _mkvEmbeddingService = mkvEmbeddingService ?? new MkvEmbeddingService(
            NullLogger<MkvEmbeddingService>.Instance);
        _siblingSequenceApprovalService = siblingSequenceApprovalService;
    }

    /// <summary>
    /// Executes a translation job. Called by TranslationWorkerService.
    /// Concurrency is managed by the worker service, not internally.
    /// </summary>
    public Task ExecuteAsync(int translationRequestId, CancellationToken cancellationToken)
        => ExecuteCore(translationRequestId, null, cancellationToken);

    internal Task ExecuteAsync(
        int translationRequestId,
        string ownershipToken,
        CancellationToken cancellationToken)
        => ExecuteCore(translationRequestId, ownershipToken, cancellationToken);

    private async Task ExecuteCore(
        int translationRequestId,
        string? ownershipToken,
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

        var executionOwnershipToken = string.IsNullOrWhiteSpace(ownershipToken)
            ? translationRequest.JobId
            : ownershipToken;

        var requestLogs = new List<TranslationRequestLog>();
        _translationPromptContextAccessor.Clear();

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

        CancellationToken jobCancellationToken = CancellationToken.None;
        CancellationToken effectiveCancellationToken = cancellationToken;
        CancellationTokenSource? linkedCts = null;
        string? temporaryFilePath = null;
        WrittenSubtitleOutput? pendingWrittenOutput = null;
        try
        {
            effectiveCancellationToken.ThrowIfCancellationRequested();

            TranslationRequest request;
            if (!string.IsNullOrWhiteSpace(executionOwnershipToken))
            {
                var startedAt = DateTime.UtcNow;
                var rowsUpdated = await _dbContext.TranslationRequests
                    .Where(item => item.Id == translationRequest.Id &&
                                   item.Status == TranslationStatus.InProgress &&
                                   item.JobId == executionOwnershipToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.IsActive, (bool?)true)
                        .SetProperty(item => item.StartedAt, startedAt)
                        .SetProperty(item => item.UpdatedAt, startedAt),
                        effectiveCancellationToken);

                if (rowsUpdated == 0)
                {
                    _logger.LogInformation(
                        "Skipping translation request {RequestId} because its worker ownership was reclaimed before execution started",
                        translationRequest.Id);
                    return;
                }

                await _dbContext.Entry(translationRequest).ReloadAsync(cancellationToken);
                if (translationRequest.Status != TranslationStatus.InProgress ||
                    !string.Equals(
                        translationRequest.JobId,
                        executionOwnershipToken,
                        StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "Skipping translation request {RequestId} because its worker ownership changed before cancellation registration",
                        translationRequest.Id);
                    return;
                }

                jobCancellationToken = _cancellationService.RegisterJob(
                    translationRequest.Id,
                    executionOwnershipToken!);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    jobCancellationToken);
                effectiveCancellationToken = linkedCts.Token;
                effectiveCancellationToken.ThrowIfCancellationRequested();

                var registrationRowsUpdated = await _dbContext.TranslationRequests
                    .Where(item => item.Id == translationRequest.Id &&
                                   item.Status == TranslationStatus.InProgress &&
                                   item.JobId == executionOwnershipToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.IsActive, (bool?)true)
                        .SetProperty(item => item.UpdatedAt, DateTime.UtcNow),
                        effectiveCancellationToken);

                if (registrationRowsUpdated == 0)
                {
                    _logger.LogInformation(
                        "Skipping translation request {RequestId} because its worker ownership was lost during cancellation registration",
                        translationRequest.Id);
                    return;
                }

                request = translationRequest;
            }
            else
            {
                jobCancellationToken = _cancellationService.RegisterJob(translationRequest.Id);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    jobCancellationToken);
                effectiveCancellationToken = linkedCts.Token;
                effectiveCancellationToken.ThrowIfCancellationRequested();

                request = await _translationRequestService.UpdateTranslationRequest(
                    translationRequest,
                    TranslationStatus.InProgress,
                    null); // Legacy/manual invocation without an attempt token

                // Set when translation actually started
                request.StartedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(effectiveCancellationToken);
            }

            translationRequest = request;

            if (translationRequest.Status != TranslationStatus.Completed)
            {
                try
                {
                    // A crashed attempt may have left an MKV rollback backup in the temp
                    // directory while the media container holds its uncommitted output.
                    // Reconcile before extraction so a retry never reads from a
                    // container that a previous attempt already modified. Requests that
                    // already committed are skipped: their leftovers belong to the
                    // commit-cleanup window and must not be reverted.
                    RollbackBackupRecovery.ReconcileRequestEmbeddedBackups(
                        translationRequest.Id,
                        _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to reconcile leftover MKV rollback backups for request {RequestId}",
                        translationRequest.Id);
                }
            }

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
SettingKeys.Translation.BatchContextAfter,
                SettingKeys.Translation.TranslateSupplementalSubtitles,
                SettingKeys.Translation.EmbedInContainer,
                SettingKeys.SubtitleExtraction.OcrTranslationPromptEnabled
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
            else if (request.WorkloadKind == TranslationWorkloadKind.CustomSource &&
                     (string.IsNullOrEmpty(subtitlePath) || !File.Exists(subtitlePath)))
            {
                _logger.LogInformation("Subtitle file not found, checking for embedded subtitles...");
                AddRequestLog("Warning", "Subtitle file not found on disk, attempting embedded subtitle extraction");

                if (request.CustomMediaItemId.HasValue)
                {
                    subtitlePath = await TryExtractCustomSourceSubtitleAsync(request, effectiveCancellationToken);
                    if (!string.IsNullOrWhiteSpace(subtitlePath))
                    {
                        temporaryFilePath = subtitlePath;
                    }
                }
                else
                {
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
            else
            {
                if (request.MediaId.HasValue)
                {
                    var streamSelectionKey = $"subtitle_stream_selection_{request.MediaId.Value}_{request.MediaType}";
                    var preferredStreamIndexSetting = await _settings.GetSetting(streamSelectionKey);
                    if (!string.IsNullOrEmpty(preferredStreamIndexSetting) && int.TryParse(preferredStreamIndexSetting, out var parsedIndex))
                    {
                        request.SourceSnapshotStreamIndex = parsedIndex;
                        _logger.LogInformation("Using preferred stream index {StreamIndex} from manual selection", parsedIndex);
                        AddRequestLog("Information", $"Using preferred stream index {parsedIndex} from manual selection");
                        await _settings.SetSetting(streamSelectionKey, "");
                    }
                }

                subtitlePath = await _sourceSubtitleResolver.ResolveReadableSourcePathAsync(
                    request,
                    effectiveCancellationToken);

                if (string.IsNullOrWhiteSpace(subtitlePath) || !File.Exists(subtitlePath))
                {
                    var errorMessage =
                        $"Source subtitle could not be resolved to a readable file for request {request.Id}.";
                    _logger.LogError(errorMessage);
                    AddRequestLog("Error", errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                request.SubtitleToTranslate = subtitlePath;
                if (SubtitleExtractionService.IsLingarrExtracted(subtitlePath))
                {
                    _logger.LogInformation("Using extracted embedded subtitle: {Path}", subtitlePath);
                    AddRequestLog("Information", $"Using extracted embedded subtitle: {subtitlePath}");
                }
            }

            var configuredSubtitleOutputMode = settings.GetValueOrDefault(SettingKeys.Translation.SubtitleOutputMode);
            var subtitleOutputMode = !string.IsNullOrWhiteSpace(configuredSubtitleOutputMode)
                ? SubtitleOutputModeHelper.Parse(configuredSubtitleOutputMode)
                : SubtitleOutputModeHelper.Parse(request.SubtitleOutputMode);
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

                var isValid = !string.IsNullOrWhiteSpace(request.SubtitleToTranslate) &&
                              _subtitleService.ValidateSubtitle(
                                  request.SubtitleToTranslate,
                                  validationOptions);

                if (!isValid)
                {
                    const string validationMessage =
                        "Configured subtitle validation blocked this translation.";
                    var validationDetails = string.IsNullOrWhiteSpace(request.SubtitleToTranslate)
                        ? "No readable source subtitle path was available. Correct or replace the source subtitle, or adjust the validation settings, then retry."
                        : $"Source subtitle '{request.SubtitleToTranslate}' failed one or more configured safety checks. Correct or replace the source subtitle, or adjust the validation settings, then retry.";

                    _logger.LogWarning(
                        "{ValidationMessage} {ValidationDetails}",
                        validationMessage,
                        validationDetails);
                    AddRequestLog("Warning", validationMessage, validationDetails);
                    throw new InvalidOperationException($"{validationMessage} {validationDetails}");
                }
            }

            // translate subtitles
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
            var translator = new SubtitleTranslationService(
                translationService,
                _logger,
                _progressService,
                _batchFallbackService,
                _deferredRepairService,
                _translationCheckpointService);
            List<SubtitleItem> subtitles;
            var attempt = 0;
            const int maxAttempts = 3;
            var excludedStreamIndices = new List<int>();

            // Generate file identifier early for logging
            var fileIdentifier = GenerateFileIdentifier(request.SubtitleToTranslate!);

            EmbeddedSubtitle? selectedSubtitle = null;
            while (true)
            {
                subtitles = await ReadSubtitlesOrEmptyForFallbackAsync(
                    request,
                    excludedStreamIndices);
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
                        request.SourceSubtitleType = SubtitleLanguageHelper.DetermineSubtitleType(selectedSubtitle);
                        _logger.LogInformation(
                            "[{FileId}] Captured subtitle metadata: Type={Type}, Entries={Entries}, Title={Title}, Forced={Forced}",
                            fileIdentifier, request.SourceSubtitleType, request.SourceSubtitleEntryCount,
                            request.SelectedStreamTitle ?? "N/A", request.IsForcedSubtitle);
                    }
                    else
                    {
                        // For external subtitle files, try to determine type from filename
                        request.SourceSubtitleType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(request.SubtitleToTranslate);
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

                    var promptContext = await BuildOcrTranslationPromptContextAsync(
                        request,
                        selectedSubtitle,
                        effectiveCancellationToken);
                    if (promptContext != null &&
                        (!settings.TryGetValue(
                             SettingKeys.SubtitleExtraction.OcrTranslationPromptEnabled,
                             out var ocrPromptEnabled) ||
                         !string.Equals(ocrPromptEnabled, "false", StringComparison.OrdinalIgnoreCase)))
                    {
                        _translationPromptContextAccessor.Current = promptContext;
                        AddRequestLog(
                            "Information",
                            "OCR-aware translation prompt applied for this OCR-derived subtitle source.");
                    }
                    else
                    {
                        _translationPromptContextAccessor.Clear();
                    }

                    if (await TryCancelObsoleteUnsafeSourceAsync(
                            request,
                            sourceIsEmbedded ? selectedSubtitle : null,
                            subtitles,
                            settings,
                            effectiveCancellationToken,
                            executionOwnershipToken))
                    {
                        return;
                    }

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
                else if (request.SourceSnapshotStreamIndex.HasValue &&
                         !excludedStreamIndices.Contains(request.SourceSnapshotStreamIndex.Value))
                {
                    excludedStreamIndices.Add(request.SourceSnapshotStreamIndex.Value);
                    _logger.LogInformation(
                        "Excluding source snapshot stream {StreamIndex} from fallback selection",
                        request.SourceSnapshotStreamIndex.Value);
                }

                var newSubtitlePath = await _extractionService.TryExtractEmbeddedSubtitleForRequestAsync(
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

                var embedInContainer = settings.TryGetValue(SettingKeys.Translation.EmbedInContainer, out var embedVal)
                && string.Equals(embedVal, "true", StringComparison.OrdinalIgnoreCase);

            var writtenOutput = await WriteSubtitles(
                request,
                translatedSubtitles,
                stripSubtitleFormatting,
                subtitleTag ?? "",
                subtitleTagShort ?? "",
                removeLanguageTag,
                writesPreservedAssOutput,
                settings,
                embedInContainer,
                effectiveCancellationToken);
            pendingWrittenOutput = writtenOutput;
            request.TranslatedSubtitle = writtenOutput.PrimaryPath;
            // Guard: TranslatedSubtitle should never equal SubtitleToTranslate
            if (string.Equals(request.TranslatedSubtitle, request.SubtitleToTranslate, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "TranslatedSubtitle was set to SubtitleToTranslate path for request {RequestId}. " +
                    "This indicates the translation output was not properly recorded. " +
                    "PrimaryPath={PrimaryPath}, OutputPaths={OutputPaths}",
                    translationRequest.Id,
                    writtenOutput.PrimaryPath,
                    string.Join(", ", writtenOutput.OutputPaths));
            }
            request.GeneratedOutputFormats = writtenOutput.GeneratedFormats;
            request.GeneratedSubtitlePaths = JsonSerializer.Serialize(writtenOutput.OutputPaths);
            AddRequestLog(
                "Information",
                $"Translation completed successfully and subtitle file was written to: {writtenOutput.PrimaryPath}");
            var completionAccepted = await HandleCompletion(request, writtenOutput, effectiveCancellationToken);
            if (!completionAccepted)
            {
                await CleanupUnclaimedOutputFilesAsync(request, writtenOutput);
                return;
            }
            pendingWrittenOutput = null;
        }
        catch (ProviderPauseException ex)
        {
            await HandleProviderPause(
                translationRequest,
                ex,
                requestLogs,
                effectiveCancellationToken,
                executionOwnershipToken);
        }
        catch (Exception ex) when (TranslationFailureClassifier.IsProviderUnavailable(ex))
        {
            var serviceType = await _settings.GetSetting(SettingKeys.Translation.ServiceType) ?? "unknown";
            var reason = $"Translation provider is temporarily unavailable: {TranslationFailureClassifier.GetFailureSummary(ex)}";
            await HandleProviderPause(
                translationRequest,
                new ProviderPauseException(serviceType, reason, DateTime.UtcNow.AddMinutes(15), ex),
                requestLogs,
                effectiveCancellationToken,
                executionOwnershipToken);
        }
        catch (TaskCanceledException)
        {
            await HandleCancellation(translationRequest, executionOwnershipToken);
        }
        catch (OperationCanceledException)
        {
            // Also catch OperationCanceledException for cooperative cancellation
            await HandleCancellation(translationRequest, executionOwnershipToken);
        }
        catch (Exception ex)
        {
            var failureException = ex;
            if (ex is MissingTranslationException missingTranslationException &&
                _siblingSequenceApprovalService != null)
            {
                var approvalResult = await _siblingSequenceApprovalService.ProcessMissingTranslationAsync(
                    translationRequest,
                    missingTranslationException,
                    effectiveCancellationToken);
                if (approvalResult.CurrentRequestCompleted)
                {
                    if (requestLogs.Count > 0)
                    {
                        _dbContext.TranslationRequestLogs.AddRange(requestLogs);
                        await _dbContext.SaveChangesAsync(effectiveCancellationToken);
                    }

                    return;
                }

                if (approvalResult.RemainingException != null)
                {
                    failureException = approvalResult.RemainingException;
                }
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
                var terminalStateLost = false;
                var failureStatusUpdated = false;
                
                // Retry logic for status update - prevents jobs getting stuck in InProgress
                // when database is temporarily unavailable
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var failureClaimed = await TryMarkGenericFailureAsync(
                            translationRequest,
                            now,
                            nextRetryAt,
                            effectiveCancellationToken,
                            executionOwnershipToken);

                        if (!failureClaimed)
                        {
                            terminalStateLost = true;
                            break;
                        }

                        failureStatusUpdated = true;

                        try
                        {
                            await _translationRequestService.ClearMediaHash(translationRequest);
                        }
                        catch (Exception cleanupEx)
                        {
                            _logger.LogWarning(cleanupEx, "Error clearing media hash during failure handling");
                        }

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

                if (terminalStateLost)
                {
                    return;
                }

                if (!failureStatusUpdated)
                {
                    _logger.LogWarning(
                        "Could not persist failed status for translation request {RequestId} after three attempts; deferring failure side effects until the job is retried",
                        translationRequest.Id);
                }

                // Persist collected logs for failed translations
                if (failureStatusUpdated && requestLogs.Count > 0)
                {
                    _dbContext.TranslationRequestLogs.AddRange(requestLogs);
                }

                // Add the failure entry as the final log message
                var failureSummary = TranslationFailureClassifier.GetFailureSummary(failureException);
                var failureMessage = $"Translation failed: {failureSummary}";
                var failureDetails = TranslationFailureClassifier.IsProviderUnavailable(failureException)
                    ? $"Root cause: translation provider unavailable.{Environment.NewLine}Summary: {failureSummary}{Environment.NewLine}{Environment.NewLine}{failureException}"
                    : failureException.ToString();
                _logger.LogError(failureException, "Translation failed for request {RequestId}", translationRequest.Id);

                if (failureStatusUpdated && translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
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
                
                if (failureStatusUpdated)
                {
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
            
            // Re-throw to ensure Hangfire knows the job failed.
            if (!ReferenceEquals(failureException, ex))
            {
                throw failureException;
            }

            throw;
        }
        finally
        {
            _translationPromptContextAccessor.Clear();
            // Always unregister the job from cooperative cancellation
            if (jobCancellationToken != CancellationToken.None)
            {
                _cancellationService.UnregisterJob(translationRequest.Id, jobCancellationToken);
            }

            linkedCts?.Dispose();

            if (pendingWrittenOutput != null)
            {
                await CleanupUnclaimedOutputFilesAsync(translationRequest, pendingWrittenOutput);
            }

            await CleanupTemporaryExtractedSubtitleAsync(translationRequest, temporaryFilePath);
        }
    }

    private async Task<List<SubtitleItem>> ReadSubtitlesOrEmptyForFallbackAsync(
        TranslationRequest request,
        List<int> excludedStreamIndices)
    {
        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate!);

            if (subtitles.Count == 0)
            {
                if (request.SourceSnapshotStreamIndex.HasValue &&
                    !excludedStreamIndices.Contains(request.SourceSnapshotStreamIndex.Value))
                {
                    excludedStreamIndices.Add(request.SourceSnapshotStreamIndex.Value);
                }

                _logger.LogWarning(
                    "Selected subtitle source {Path} contained no readable dialogue entries. Attempting embedded fallback if available.",
                    request.SubtitleToTranslate);
            }

            return subtitles;
        }
        catch (ArgumentException ex) when (ex.Message.Contains(
            "No valid subtitles found in SSA format",
            StringComparison.OrdinalIgnoreCase))
        {
            if (request.SourceSnapshotStreamIndex.HasValue &&
                !excludedStreamIndices.Contains(request.SourceSnapshotStreamIndex.Value))
            {
                excludedStreamIndices.Add(request.SourceSnapshotStreamIndex.Value);
            }

            _logger.LogWarning(
                ex,
                "Selected ASS/SSA subtitle source {Path} contained no readable dialogue entries. Attempting embedded fallback if available.",
                request.SubtitleToTranslate);
            return [];
        }
    }

    private async Task<WrittenSubtitleOutput> WriteSubtitles(TranslationRequest translationRequest,
        List<SubtitleItem> translatedSubtitles,
        bool stripSubtitleFormatting,
        string subtitleTag,
        string subtitleTagShort,
        bool removeLanguageTag,
        bool writesPreservedAssOutput,
        IReadOnlyDictionary<string, string> settings,
        bool embedInContainer,
        CancellationToken cancellationToken)
    {
        var stagedOutputs = new List<StagedSubtitleOutput>();
        var stagedEmbeddedOutputs = new List<StagedEmbeddedSubtitleOutput>();

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
                var paths = (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload
                    ? await _uploadWorkspaceService.GetOutputPathsAsync(
                        translationRequest,
                        targetLanguage,
                        subtitleTag,
                        subtitleTagShort,
                        outputFormat,
                        cancellationToken)
                    : _subtitleService.CreateFallbackPaths(
                        await ResolveOutputBasePathAsync(translationRequest, cancellationToken),
                        targetLanguage,
                        subtitleTag,
                        subtitleTagShort,
                        outputFormat,
                        SubtitleLanguageHelper.GetSupplementalOutputCaption(
                            translationRequest.SourceSubtitleType)))
                    .Where(path => !IsSamePath(path, translationRequest.SubtitleToTranslate))
                    .ToList();

Exception? lastException = null;
                bool success = false;
                bool allPathsTooLong = true;
                var anyPathExceedsLimit = paths.Any(p => _mkvEmbeddingService.WouldExceedPathLimit(p));
                var embedWhenPathTooLong = settings.TryGetValue(SettingKeys.Translation.EmbedWhenPathTooLong, out var tooLongVal)
                    && string.Equals(tooLongVal, "true", StringComparison.OrdinalIgnoreCase);

                if (embedInContainer || (embedWhenPathTooLong && anyPathExceedsLimit))
                {
                    var embeddedOutput = await StageSubtitleForMkvEmbeddingAsync(
                        translationRequest,
                        renderSubtitles,
                        outputFormat,
                        outputStripFormatting,
                        translationRequest.TargetLanguage,
                        cancellationToken);

                    if (embeddedOutput != null)
                    {
                        success = true;
                        allPathsTooLong = false;
                        writtenOutputs.Add((outputFormat, embeddedOutput.OutputPath));
                        stagedEmbeddedOutputs.Add(embeddedOutput);
                    }
                    else
                    {
                        lastException = new PathTooLongException(
                            $"All output file paths exceed filesystem limits for format {outputFormat}, " +
                            "and MKV embedding was not possible.");

                        _logger.LogWarning(
                            "Path exceeds filesystem limit and MKV embedding failed for format {Format} on request {RequestId}. Falling back to path write attempts.",
                            outputFormat,
                            translationRequest.Id);
                    }
                }

                if (!success)
                {
                    if (!anyPathExceedsLimit)
                    {
                        foreach (var path in paths)
                        {
                            string? stagingPath = null;
                            string? publicationPath = null;
                            var preserveStagingArtifact = false;
                            try
                            {
                                stagingPath = _translationDiagnosticsService.CreateQuarantinePath(
                                    translationRequest.Id,
                                    path);

                                EnsureParentDirectory(stagingPath);
                                await _subtitleService.WriteSubtitles(
                                    stagingPath,
                                    renderSubtitles,
                                    outputStripFormatting);

                                var validationResult = await _subtitleQualityValidatorService.ValidateAsync(
                                    new SubtitleQualityValidationRequest
                                    {
                                        SourcePath = translationRequest.SubtitleToTranslate!,
                                        TargetPath = stagingPath,
                                        SourceLanguage = translationRequest.SourceLanguage,
                                        TargetLanguage = translationRequest.TargetLanguage,
                                        OutputFormat = outputFormat
                                    },
                                    cancellationToken);

                                if (!validationResult.IsValid)
                                {
                                    await RecordOutputValidationFailureAsync(
                                        translationRequest,
                                        path,
                                        stagingPath,
                                        outputFormat,
                                        validationResult,
                                        cancellationToken);

                                    var qualityGateSetting = await _settings.GetSetting(SettingKeys.Translation.EnablePostTranslationQualityGate);
                                    var qualityGateEnabled = string.Equals(qualityGateSetting, "true", StringComparison.OrdinalIgnoreCase);

                                    if (qualityGateEnabled)
                                    {
                                        preserveStagingArtifact = true;
                                        throw new TranslationException(
                                            $"Generated subtitle failed quality validation before publishing: {validationResult.Summary}");
                                    }

                                    _logger.LogWarning(
                                        "Post-translation quality gate is disabled. Publishing subtitle despite validation failure: {Summary}",
                                        validationResult.Summary);
                                }

                                EnsureParentDirectory(path);
                                publicationPath = CreatePublicationPath(path);
                                File.Copy(stagingPath, publicationPath!, true);
                                success = true;
                                allPathsTooLong = false;
                                writtenOutputs.Add((outputFormat, path));
                                stagedOutputs.Add(new StagedSubtitleOutput(
                                    outputFormat,
                                    path,
                                    stagingPath!,
                                    publicationPath!,
                                    paths,
                                    subtitleTag,
                                    subtitleTagShort,
                                    translationRequest.SubtitleToTranslate));
                                break;
                            }
                            catch (TranslationException)
                            {
                                if (!preserveStagingArtifact)
                                {
                                    DeleteFileIfExists(stagingPath);
                                }

                                DeleteFileIfExists(publicationPath);

                                throw;
                            }
                            catch (PathTooLongException ex)
                            {
                                DeleteFileIfExists(stagingPath);
                                DeleteFileIfExists(publicationPath);
                                _logger.LogWarning("Path too long: {Path}. Trying fallback...", path);
                                lastException = ex;
                            }
                            catch (Exception ex)
                            {
                                DeleteFileIfExists(stagingPath);
                                DeleteFileIfExists(publicationPath);
                                _logger.LogWarning(ex, "Failed to write subtitle to {Path}. Trying fallback...", path);
                                lastException = ex;
                                allPathsTooLong = false;
                            }
                        }
                    }
                }

                if (!success && (embedInContainer || allPathsTooLong))
                {
                    var embeddedOutput = await StageSubtitleForMkvEmbeddingAsync(
                        translationRequest,
                        renderSubtitles,
                        outputFormat,
                        outputStripFormatting,
                        translationRequest.TargetLanguage,
                        cancellationToken);

                    if (embeddedOutput != null)
                    {
                        success = true;
                        writtenOutputs.Add((outputFormat, embeddedOutput.OutputPath));
                        stagedEmbeddedOutputs.Add(embeddedOutput);
                    }
                }

                if (!success)
                {
                    if (lastException is PathTooLongException)
                    {
                        throw new PathTooLongException(
                            $"The subtitle filename exceeds the filesystem limit (255 bytes). " +
                            "Enable 'Embed subtitles in media container' in Settings, " +
                            "or rename the media file to be shorter.");
                    }

                    if (lastException != null)
                    {
                        throw lastException;
                    }

                    throw new Exception($"Failed to write subtitle to any fallback path for format {outputFormat}.");
                }

            }

            var primaryPath = writtenOutputs
                .OrderByDescending(output =>
                    string.Equals(
                        SubtitleOutputModeHelper.NormalizeFormat(output.Format),
                        SubtitleOutputModeHelper.NormalizeFormat(translationRequest.SourceSubtitleFormat),
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(output => output.Path.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase))
                .Select(output => output.Path)
                .First();

            var generatedFormats = SubtitleOutputModeHelper.SerializeFormats(writtenOutputs.Select(output => output.Format));

            _logger.LogInformation(
                "TranslateJob completed and created subtitle outputs: |Green|{SubtitleOutputs}|/Green|",
                string.Join(", ", writtenOutputs.Select(output => output.Path)));
            return new WrittenSubtitleOutput(
                primaryPath,
                generatedFormats,
                writtenOutputs.Select(output => output.Path).ToList(),
                stagedOutputs,
                stagedEmbeddedOutputs);
        }
        catch (Exception e)
        {
            CleanupStagedOutputArtifacts(stagedOutputs, stagedEmbeddedOutputs);
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private void DeleteStaleTaggedFallbackSiblings(
        IEnumerable<string> fallbackPaths,
        string publishedPath,
        string? sourcePath,
        string subtitleTag,
        string subtitleTagShort)
    {
        var configuredTags = new[] { subtitleTag, subtitleTagShort }
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().Trim('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configuredTags.Count == 0)
        {
            return;
        }

        foreach (var stalePath in fallbackPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(stalePath) ||
                IsSamePath(stalePath, publishedPath) ||
                IsSamePath(stalePath, sourcePath) ||
                !HasConfiguredSubtitleTag(stalePath, configuredTags))
            {
                continue;
            }

            try
            {
                File.Delete(stalePath);
                _logger.LogInformation(
                    "Deleted stale tagged subtitle output {Path} after publishing {PublishedPath}",
                    stalePath,
                    publishedPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete stale tagged subtitle output {Path} after publishing {PublishedPath}",
                    stalePath,
                    publishedPath);
            }
        }
    }

    private static bool HasConfiguredSubtitleTag(string path, IReadOnlyCollection<string> configuredTags)
    {
        var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return configuredTags.Any(tag =>
            fileName.EndsWith($".{tag}", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains($".{tag}.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSamePath(string path, string? otherPath)
    {
        return !string.IsNullOrWhiteSpace(otherPath) &&
               string.Equals(
                   Path.GetFullPath(path),
                   Path.GetFullPath(otherPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<StagedEmbeddedSubtitleOutput?> StageSubtitleForMkvEmbeddingAsync(
        TranslationRequest translationRequest,
        List<SubtitleItem> renderSubtitles,
        string outputFormat,
        bool outputStripFormatting,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var basePath = await ResolveOutputBasePathAsync(translationRequest, cancellationToken);
        if (string.IsNullOrEmpty(basePath))
        {
            _logger.LogWarning("Cannot embed subtitle in MKV: no base media path resolved for request {RequestId}", translationRequest.Id);
            return null;
        }

        var extension = Path.GetExtension(basePath);
        if (!string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Cannot embed subtitle in container: media file is not MKV ({Extension}). Request {RequestId}",
                extension,
                translationRequest.Id);
            return null;
        }

        if (!File.Exists(basePath))
        {
            _logger.LogWarning(
                "Cannot embed subtitle in MKV: media file not found at {Path}. Request {RequestId}",
                basePath,
                translationRequest.Id);
            return null;
        }

        var normalizedLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        if (string.IsNullOrEmpty(normalizedLanguage))
        {
            normalizedLanguage = targetLanguage;
        }

        string? tempSubtitlePath = null;
        try
        {
            var subtitleExtension = SubtitleOutputModeHelper.NormalizeFormat(outputFormat);
            if (string.IsNullOrEmpty(subtitleExtension))
            {
                subtitleExtension = ".srt";
            }

            tempSubtitlePath = Path.Combine(
                Path.GetTempPath(),
                $"lingarr_embed_{translationRequest.Id}_{Guid.NewGuid():N}{subtitleExtension}");

            EnsureParentDirectory(tempSubtitlePath);
            await _subtitleService.WriteSubtitles(tempSubtitlePath, renderSubtitles, outputStripFormatting);

            var trackName = $"{normalizedLanguage} (Lingarr)";
            var embeddedMarker = $"mkv-embedded:stream0";
            return new StagedEmbeddedSubtitleOutput(
                $"{embeddedMarker}|{basePath}",
                basePath,
                tempSubtitlePath,
                normalizedLanguage,
                trackName);
        }
        catch (OperationCanceledException)
        {
            DeleteFileIfExists(tempSubtitlePath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteFileIfExists(tempSubtitlePath);
            _logger.LogWarning(ex, "Failed to stage subtitle for MKV embedding for request {RequestId}", translationRequest.Id);
            return null;
        }
    }

    private async Task<string> ResolveOutputBasePathAsync(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        if (translationRequest.WorkloadKind != TranslationWorkloadKind.Library ||
            string.IsNullOrWhiteSpace(translationRequest.SubtitleToTranslate) ||
            !_embeddedSubtitleCacheService.IsManagedCachePath(translationRequest.SubtitleToTranslate) ||
            !translationRequest.MediaId.HasValue)
        {
            return translationRequest.SubtitleToTranslate!;
        }

        if (translationRequest.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .AsNoTracking()
                .Where(item => item.Id == translationRequest.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);

            var moviePath = ResolveMediaFilePath(movie?.Path, movie?.FileName);
            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                return moviePath;
            }
        }

        if (translationRequest.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .AsNoTracking()
                .Where(item => item.Id == translationRequest.MediaId.Value)
                .Select(item => new { item.Path, item.FileName })
                .FirstOrDefaultAsync(cancellationToken);

            var episodePath = ResolveMediaFilePath(episode?.Path, episode?.FileName);
            if (!string.IsNullOrWhiteSpace(episodePath))
            {
                return episodePath;
            }
        }

        _logger.LogWarning(
            "Could not resolve media file path for embedded-cache translation request {RequestId}. Falling back to cache source path.",
            translationRequest.Id);
        return translationRequest.SubtitleToTranslate!;
    }

    private static string? ResolveMediaFilePath(string? directoryPath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return ResolveExistingMediaFilePath(
                Path.GetDirectoryName(fileName),
                Path.GetFileName(fileName)) ?? fileName;
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        return ResolveExistingMediaFilePath(directoryPath, fileName)
               ?? Path.Combine(directoryPath, fileName);
    }

    private static string? ResolveExistingMediaFilePath(string? directoryPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var candidatePath = Path.Combine(directoryPath, fileName);
        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }

        if (!Directory.Exists(directoryPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(directoryPath)
            .FirstOrDefault(path =>
                MediaFileExtensions.Contains(Path.GetExtension(path)) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    fileName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private async Task RecordOutputValidationFailureAsync(
        TranslationRequest translationRequest,
        string finalPath,
        string quarantinePath,
        string outputFormat,
        SubtitleQualityValidationResult validationResult,
        CancellationToken cancellationToken)
    {
        var details = new
        {
            validationResult.SourceEntryCount,
            validationResult.TargetEntryCount,
            validationResult.MinimumTargetEntryCount,
            validationResult.IssueTypes
        };

        await _translationDiagnosticsService.RecordAsync(
            new TranslationDiagnosticEventRequest
            {
                TranslationRequestId = translationRequest.Id,
                MediaId = translationRequest.MediaId,
                MediaType = translationRequest.MediaType,
                Title = translationRequest.Title,
                Stage = "post_write_validation",
                Provider = await _settings.GetSetting(SettingKeys.Translation.ServiceType),
                SourcePath = translationRequest.SubtitleToTranslate,
                TargetPath = finalPath,
                QuarantinePath = quarantinePath,
                OutputFormat = outputFormat,
                SourceSnapshotIdentity = translationRequest.SourceSnapshotIdentity,
                SourceSnapshotFingerprint = translationRequest.SourceSnapshotFingerprint,
                ReasonCode = validationResult.IssueTypes.FirstOrDefault()
                    ?? SubtitleQualityIssueCodes.ValidationError,
                Summary = validationResult.Summary,
                SampleLines = validationResult.SampleLines,
                DetailsJson = JsonSerializer.Serialize(details)
            },
            cancellationToken);
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string CreatePublicationPath(string finalPath)
    {
        return $"{finalPath}.lingarr-publish-{Guid.NewGuid():N}.tmp";
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
            var plainTextLines = PlainTextSubtitleOutputRenderer.ConvertToPlainTextLines(translatedText);

            if (PlainTextSubtitleOutputRenderer.ShouldSkipSubtitle(plainTextLines))
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

    private sealed record StagedEmbeddedSubtitleOutput(
        string OutputPath,
        string MediaPath,
        string SubtitlePath,
        string LanguageCode,
        string TrackName);

    private sealed record StagedSubtitleOutput(
        string Format,
        string FinalPath,
        string StagingPath,
        string PublicationPath,
        IReadOnlyCollection<string> CandidatePaths,
        string SubtitleTag,
        string SubtitleTagShort,
        string? SourcePath);

    private sealed record PublishedSubtitleArtifact(
        string FinalPath,
        string? BackupPath,
        bool HadExistingFile,
        string ExpectedContentHash);

    private sealed class EmbeddedPublicationTransaction
    {
        public List<EmbeddedPublicationBackup> Backups { get; } = [];
    }

    private sealed class EmbeddedPublicationBackup
    {
        public EmbeddedPublicationBackup(
            string mediaPath,
            string backupPath,
            string originalHash)
        {
            MediaPath = mediaPath;
            BackupPath = backupPath;
            OriginalHash = originalHash;
        }

        public string MediaPath { get; }

        public string BackupPath { get; }

        public string OriginalHash { get; }

        public string? ExpectedPublishedHash { get; set; }
    }

    private sealed record WrittenSubtitleOutput(
        string PrimaryPath,
        string GeneratedFormats,
        IReadOnlyCollection<string> OutputPaths,
        IReadOnlyCollection<StagedSubtitleOutput> StagedOutputs,
        IReadOnlyCollection<StagedEmbeddedSubtitleOutput> StagedEmbeddedOutputs);

    private Task CleanupUnclaimedOutputFilesAsync(
        TranslationRequest translationRequest,
        WrittenSubtitleOutput writtenOutput)
    {
        CleanupStagedOutputArtifacts(writtenOutput.StagedOutputs, writtenOutput.StagedEmbeddedOutputs);

        return Task.CompletedTask;
    }

    private void CleanupStagedOutputArtifacts(
        IReadOnlyCollection<StagedSubtitleOutput> stagedOutputs,
        IReadOnlyCollection<StagedEmbeddedSubtitleOutput> stagedEmbeddedOutputs)
    {
        foreach (var stagedOutput in stagedOutputs)
        {
            DeleteFileIfExists(stagedOutput.StagingPath);
            DeleteFileIfExists(stagedOutput.PublicationPath);
        }

        foreach (var embeddedOutput in stagedEmbeddedOutputs)
        {
            DeleteFileIfExists(embeddedOutput.SubtitlePath);
        }
    }

    private void DeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete subtitle artifact {Path}", path);
        }
    }

    private void PublishStagedOutputs(
        WrittenSubtitleOutput writtenOutput,
        List<PublishedSubtitleArtifact> publishedOutputs,
        int requestId,
        CancellationToken cancellationToken)
    {
        foreach (var stagedOutput in writtenOutput.StagedOutputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(stagedOutput.PublicationPath))
            {
                throw new FileNotFoundException(
                    $"Staged subtitle output was not found for format {stagedOutput.Format}.",
                    stagedOutput.PublicationPath);
            }

            try
            {
                RollbackBackupRecovery.ReconcileSubtitleBackups(
                    stagedOutput.FinalPath,
                    requestId,
                    _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to reconcile stale rollback backups for {Path}",
                    stagedOutput.FinalPath);
            }

            EnsureParentDirectory(stagedOutput.FinalPath);
            var backupPath = CreateRollbackBackupPath(stagedOutput.FinalPath);
            var hadExistingFile = File.Exists(stagedOutput.FinalPath);
            var originalHash = hadExistingFile ? ComputeFileHash(stagedOutput.FinalPath) : null;
            var expectedContentHash = ComputeFileHash(stagedOutput.PublicationPath);
            try
            {
                if (hadExistingFile)
                {
                    File.Move(stagedOutput.FinalPath, backupPath);
                    // The manifest is written as soon as the backup exists and before
                    // the published file replaces the final path, so every crash window
                    // stays reconcilable.
                    RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
                    {
                        RequestId = requestId,
                        TargetPath = stagedOutput.FinalPath,
                        OriginalHash = originalHash,
                        ExpectedPublishedHash = expectedContentHash
                    });
                }

                File.Move(stagedOutput.PublicationPath, stagedOutput.FinalPath, true);
                publishedOutputs.Add(new PublishedSubtitleArtifact(
                    stagedOutput.FinalPath,
                    backupPath,
                    hadExistingFile,
                    expectedContentHash));
                DeleteFileIfExists(stagedOutput.StagingPath);
            }
            catch
            {
                RestorePublishedSubtitleArtifact(
                    new PublishedSubtitleArtifact(
                        stagedOutput.FinalPath,
                        backupPath,
                        hadExistingFile,
                        expectedContentHash));
                throw;
            }
        }

    }

    private void FinalizePublishedOutputs(
        WrittenSubtitleOutput writtenOutput,
        IReadOnlyCollection<PublishedSubtitleArtifact> publishedOutputs)
    {
        foreach (var publishedOutput in publishedOutputs)
        {
            RollbackBackupRecovery.DeleteBackup(publishedOutput.BackupPath);
        }

        foreach (var stagedOutput in writtenOutput.StagedOutputs)
        {
            DeleteStaleTaggedFallbackSiblings(
                stagedOutput.CandidatePaths,
                stagedOutput.FinalPath,
                stagedOutput.SourcePath,
                stagedOutput.SubtitleTag,
                stagedOutput.SubtitleTagShort);
        }
    }

    private void CleanupPublicationBackups(
        IReadOnlyCollection<PublishedSubtitleArtifact> publishedOutputs)
    {
        foreach (var publishedOutput in publishedOutputs)
        {
            RollbackBackupRecovery.DeleteBackup(publishedOutput.BackupPath);
        }
    }

    private void RollbackPublishedOutputs(
        IReadOnlyList<PublishedSubtitleArtifact> publishedOutputs)
    {
        for (var index = publishedOutputs.Count - 1; index >= 0; index--)
        {
            try
            {
                RestorePublishedSubtitleArtifact(publishedOutputs[index]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to roll back subtitle publication for {Path}",
                    publishedOutputs[index].FinalPath);
            }
        }
    }

    private void RestorePublishedSubtitleArtifact(PublishedSubtitleArtifact publishedOutput)
    {
        var finalExists = File.Exists(publishedOutput.FinalPath);
        var publicationStillOwnsFinal = false;
        if (finalExists)
        {
            if (!TryComputeFileHash(publishedOutput.FinalPath, out var currentHash))
            {
                _logger.LogWarning(
                    "Could not verify ownership of subtitle output {Path} during rollback; leaving the current file and backup untouched",
                    publishedOutput.FinalPath);
                return;
            }

            publicationStillOwnsFinal = string.Equals(
                currentHash,
                publishedOutput.ExpectedContentHash,
                StringComparison.Ordinal);
        }

        var canRestoreOriginal = publishedOutput.HadExistingFile &&
                                 publishedOutput.BackupPath != null &&
                                 File.Exists(publishedOutput.BackupPath);
        if (!finalExists || publicationStillOwnsFinal)
        {
            if (publicationStillOwnsFinal && (canRestoreOriginal || !publishedOutput.HadExistingFile))
            {
                DeleteFileIfExists(publishedOutput.FinalPath);
            }
            else if (publicationStillOwnsFinal && publishedOutput.HadExistingFile)
            {
                // The original existed but its backup is gone: the current file is the
                // only surviving content — never delete it.
                _logger.LogWarning(
                    "Keeping subtitle output {Path} during rollback: the original existed but its rollback backup is missing",
                    publishedOutput.FinalPath);
            }

            if (canRestoreOriginal)
            {
                try
                {
                    EnsureParentDirectory(publishedOutput.FinalPath);
                    File.Move(publishedOutput.BackupPath, publishedOutput.FinalPath);
                    RollbackBackupRecovery.DeleteManifest(publishedOutput.BackupPath);
                }
                catch (Exception ex)
                {
                    // The backup is deliberately kept: it holds the pre-publication original.
                    _logger.LogWarning(
                        ex,
                        "Failed to restore previous subtitle output {Path} after publication rollback; keeping rollback backup {BackupPath}",
                        publishedOutput.FinalPath,
                        publishedOutput.BackupPath);
                }
            }
        }
        else if (publishedOutput.BackupPath != null &&
                 File.Exists(publishedOutput.BackupPath))
        {
            // A foreign writer owns the final file: keep the backup — it holds the
            // pre-publication original and must survive for manual or automatic recovery.
            _logger.LogWarning(
                "Rollback skipped for {Path}: current file was not produced by this publication. Keeping rollback backup {BackupPath}.",
                publishedOutput.FinalPath,
                publishedOutput.BackupPath);
        }
    }

    private static string CreateRollbackBackupPath(string finalPath)
    {
        return $"{finalPath}.lingarr-rollback-{Guid.NewGuid():N}.bak";
    }

    private static IEnumerable<string> GetPublicationPaths(WrittenSubtitleOutput writtenOutput)
    {
        return writtenOutput.StagedOutputs
            .Select(output => output.FinalPath)
            .Concat(writtenOutput.StagedEmbeddedOutputs.Select(output => output.MediaPath));
    }

    private async Task<EmbeddedPublicationTransaction?> CreateEmbeddedPublicationTransactionAsync(
        WrittenSubtitleOutput writtenOutput,
        string? ownershipToken,
        int requestId,
        CancellationToken cancellationToken)
    {
        var mediaPaths = writtenOutput.StagedEmbeddedOutputs
            .Select(output => output.MediaPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mediaPaths.Count == 0)
        {
            return null;
        }

        var transaction = new EmbeddedPublicationTransaction();
        try
        {
            foreach (var mediaPath in mediaPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(mediaPath))
                {
                    throw new FileNotFoundException(
                        $"Media container for MKV embedding was not found: {mediaPath}",
                        mediaPath);
                }

                try
                {
                    RollbackBackupRecovery.ReconcileEmbeddedBackups(mediaPath, requestId, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to reconcile stale MKV rollback backups for {MediaPath}",
                        mediaPath);
                }

                var backupPath = CreateEmbeddedPublicationBackupPath(ownershipToken);
                var originalHash = ComputeFileHash(mediaPath);
                try
                {
                    await CopyFileAsync(mediaPath, backupPath, cancellationToken);
                }
                catch
                {
                    DeleteFileIfExists(backupPath);
                    throw;
                }

                transaction.Backups.Add(new EmbeddedPublicationBackup(
                    mediaPath,
                    backupPath,
                    originalHash));
                RollbackBackupRecovery.WriteManifest(backupPath, new RollbackBackupManifest
                {
                    RequestId = requestId,
                    TargetPath = mediaPath,
                    OriginalHash = originalHash
                });
            }

            return transaction;
        }
        catch
        {
            RollbackEmbeddedPublication(transaction);
            throw;
        }
    }

    private void RollbackEmbeddedPublication(EmbeddedPublicationTransaction? transaction)
    {
        if (transaction == null)
        {
            return;
        }

        foreach (var backup in transaction.Backups.AsEnumerable().Reverse())
        {
            try
            {
                var shouldRestore = !File.Exists(backup.MediaPath);
                string? currentHash = null;
                if (!shouldRestore)
                {
                    currentHash = ComputeFileHash(backup.MediaPath);
                    shouldRestore = backup.ExpectedPublishedHash == null
                        ? !string.Equals(currentHash, backup.OriginalHash, StringComparison.Ordinal)
                        : string.Equals(currentHash, backup.ExpectedPublishedHash, StringComparison.Ordinal);
                }

                if (shouldRestore && File.Exists(backup.BackupPath))
                {
                    if (File.Exists(backup.MediaPath))
                    {
                        File.Delete(backup.MediaPath);
                    }

                    File.Move(backup.BackupPath, backup.MediaPath);
                    RollbackBackupRecovery.DeleteManifest(backup.BackupPath);
                }
                else if (currentHash != null &&
                         string.Equals(currentHash, backup.OriginalHash, StringComparison.Ordinal))
                {
                    // Media is untouched: the backup is a redundant copy.
                    RollbackBackupRecovery.DeleteBackup(backup.BackupPath);
                }
                else
                {
                    // A foreign writer owns the media: keep the backup — it holds the
                    // pre-publication original and must survive for recovery.
                    _logger.LogWarning(
                        "Keeping MKV rollback backup {BackupPath} for {MediaPath}: current file was not produced by this publication",
                        backup.BackupPath,
                        backup.MediaPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to roll back MKV publication for {MediaPath}",
                    backup.MediaPath);
            }
        }
    }

    private void CleanupEmbeddedPublicationBackups(EmbeddedPublicationTransaction? transaction)
    {
        if (transaction == null)
        {
            return;
        }

        foreach (var backup in transaction.Backups)
        {
            RollbackBackupRecovery.DeleteBackup(backup.BackupPath);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        EnsureParentDirectory(destinationPath);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool TryComputeFileHash(string path, out string hash)
    {
        try
        {
            hash = ComputeFileHash(path);
            return true;
        }
        catch (Exception)
        {
            hash = string.Empty;
            return false;
        }
    }

    private static string CreateEmbeddedPublicationBackupPath(string? ownershipToken)
    {
        var token = string.IsNullOrWhiteSpace(ownershipToken) ? "legacy" : ownershipToken;
        return Path.Combine(
            Path.GetTempPath(),
            $"lingarr_normal_embed_{token}_{Guid.NewGuid():N}.bak");
    }

    private async Task PublishStagedEmbeddedOutputsAsync(
        WrittenSubtitleOutput writtenOutput,
        EmbeddedPublicationTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var embeddedOutputs = writtenOutput.StagedEmbeddedOutputs.ToList();
        if (embeddedOutputs.Count == 0)
        {
            return;
        }

        if (transaction == null)
        {
            throw new InvalidOperationException(
                "MKV embedding cannot be published without a reversible publication transaction.");
        }

        foreach (var embeddedOutput in embeddedOutputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(embeddedOutput.SubtitlePath))
            {
                throw new FileNotFoundException(
                    "Staged subtitle for MKV embedding was not found.",
                    embeddedOutput.SubtitlePath);
            }
        }

        var mediaPaths = embeddedOutputs
            .Select(output => output.MediaPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mediaPaths.Count != 1)
        {
            throw new TranslationException(
                "MKV embedding was not published because one translation request staged subtitles for multiple media containers. " +
                "The request will remain retryable and no container embedding was started.");
        }

        if (embeddedOutputs.Count == 1)
        {
            var embeddedOutput = embeddedOutputs[0];
            var publication = transaction.Backups.Single(backup =>
                string.Equals(
                    backup.MediaPath,
                    embeddedOutput.MediaPath,
                    StringComparison.OrdinalIgnoreCase));
            var result = await _mkvEmbeddingService.EmbedSubtitleAsync(
                embeddedOutput.MediaPath,
                embeddedOutput.SubtitlePath,
                embeddedOutput.LanguageCode,
                embeddedOutput.TrackName,
                cancellationToken);

            if (result is null || !result.Success)
            {
                throw new TranslationException(
                    $"MKV embedding failed for request output {embeddedOutput.OutputPath}: " +
                    $"{result?.Error ?? "The embedding service returned no result."}");
            }

            publication.ExpectedPublishedHash = ComputeFileHash(embeddedOutput.MediaPath);
            RollbackBackupRecovery.UpdateManifest(
                publication.BackupPath,
                manifest => manifest.ExpectedPublishedHash = publication.ExpectedPublishedHash);

            DeleteFileIfExists(embeddedOutput.SubtitlePath);
            _logger.LogInformation(
                "Successfully embedded subtitle in MKV container. Language: {Language}, Track: {TrackName}",
                embeddedOutput.LanguageCode,
                embeddedOutput.TrackName);
            return;
        }

        var batchInputs = embeddedOutputs
            .Select(output => new MkvSubtitleInput(
                output.SubtitlePath,
                output.LanguageCode,
                output.TrackName))
            .ToList();
        var batchResult = await _mkvEmbeddingService.EmbedSubtitlesAsync(
            mediaPaths[0],
            batchInputs,
            cancellationToken);

        if (batchResult is null || !batchResult.Success)
        {
            var error = batchResult?.Error ?? "The batch embedding service returned no result.";
            _logger.LogError(
                "Batch MKV embedding failed for request outputs {OutputPaths}. " +
                "The container was not accepted as published and the request will be retried. Error: {Error}",
                string.Join(", ", embeddedOutputs.Select(output => output.OutputPath)),
                error);
            throw new TranslationException(
                $"Batch MKV embedding failed for request outputs " +
                $"{string.Join(", ", embeddedOutputs.Select(output => output.OutputPath))}. " +
                $"No multi-output container publication was accepted; the request remains retryable. {error}");
        }

        var batchPublication = transaction.Backups.Single(backup =>
            string.Equals(
                backup.MediaPath,
                mediaPaths[0],
                StringComparison.OrdinalIgnoreCase));
        batchPublication.ExpectedPublishedHash = ComputeFileHash(mediaPaths[0]);
        RollbackBackupRecovery.UpdateManifest(
            batchPublication.BackupPath,
            manifest => manifest.ExpectedPublishedHash = batchPublication.ExpectedPublishedHash);

        foreach (var embeddedOutput in embeddedOutputs)
        {
            DeleteFileIfExists(embeddedOutput.SubtitlePath);
            _logger.LogInformation(
                "Successfully embedded subtitle in MKV container. Language: {Language}, Track: {TrackName}",
                embeddedOutput.LanguageCode,
                embeddedOutput.TrackName);
        }
    }

    internal async Task<bool> TryMarkGenericFailureAsync(
        TranslationRequest request,
        DateTime failedAt,
        DateTime nextRetryAt,
        CancellationToken cancellationToken,
        string? ownershipToken = null)
    {
        ownershipToken = ResolveOwnershipToken(request, ownershipToken);
        var query = _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id &&
                          item.Status == TranslationStatus.InProgress &&
                          item.JobId == ownershipToken);

        var rowsUpdated = await query
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TranslationStatus.Failed)
                .SetProperty(item => item.IsActive, (bool?)null)
                .SetProperty(item => item.RetryCount, item => item.RetryCount + 1)
                .SetProperty(item => item.FailedAt, failedAt)
                .SetProperty(item => item.NextRetryAt, nextRetryAt)
                .SetProperty(item => item.CompletedAt, (DateTime?)null)
                .SetProperty(item => item.PausedAt, (DateTime?)null)
                .SetProperty(item => item.PauseReason, (string?)null)
                .SetProperty(item => item.PausedProvider, (string?)null)
                .SetProperty(item => item.JobId, (string?)null)
                .SetProperty(item => item.UpdatedAt, failedAt),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            await _dbContext.Entry(request).ReloadAsync(cancellationToken);
            _logger.LogInformation(
                "Skipping failure for translation request {RequestId} because it is no longer active",
                request.Id);
            return false;
        }

        await _dbContext.Entry(request).ReloadAsync(cancellationToken);
        await _translationRequestService.UpdateActiveCount();
        return true;
    }

    internal async Task<bool> HandleProviderPause(
        TranslationRequest request,
        ProviderPauseException exception,
        List<TranslationRequestLog> requestLogs,
        CancellationToken cancellationToken,
        string? ownershipToken = null)
    {
        ownershipToken = ResolveOwnershipToken(request, ownershipToken);
        var now = DateTime.UtcNow;
        var reason = string.IsNullOrWhiteSpace(exception.Reason)
            ? "Translation provider paused the request."
            : exception.Reason;

        var currentRequestQuery = _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(translationRequest => translationRequest.Id == request.Id &&
                                         translationRequest.JobId == ownershipToken);

        var currentRequest = await currentRequestQuery.FirstOrDefaultAsync(cancellationToken);

        if (currentRequest == null)
        {
            _logger.LogWarning(
                "Paused translation request {RequestId} was not found during pause handling",
                request.Id);
            return false;
        }

        var apiDelay = (exception.ResumeAt ?? now.AddSeconds(60)) - now;
        var pauseDelay = currentRequest.RetryCount switch
        {
            0 => apiDelay,
            1 => TimeSpan.FromSeconds(120),
            2 => TimeSpan.FromSeconds(240),
            _ => TimeSpan.FromSeconds(300)
        };
        var resumeAt = now + pauseDelay;
        var pauseQuery = _dbContext.TranslationRequests
            .Where(translationRequest => translationRequest.Id == request.Id &&
                                         translationRequest.Status == TranslationStatus.InProgress &&
                                         translationRequest.RetryCount == currentRequest.RetryCount &&
                                         translationRequest.JobId == ownershipToken);

        var rowsUpdated = await pauseQuery
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(translationRequest => translationRequest.Status, TranslationStatus.Paused)
                .SetProperty(translationRequest => translationRequest.IsActive, (bool?)true)
                .SetProperty(translationRequest => translationRequest.PausedAt, now)
                .SetProperty(translationRequest => translationRequest.PauseReason, reason)
                .SetProperty(translationRequest => translationRequest.PausedProvider, exception.Provider)
                .SetProperty(translationRequest => translationRequest.NextRetryAt, resumeAt)
                .SetProperty(translationRequest => translationRequest.CompletedAt, (DateTime?)null)
                .SetProperty(translationRequest => translationRequest.RetryCount, translationRequest => translationRequest.RetryCount + 1)
                .SetProperty(translationRequest => translationRequest.UpdatedAt, now),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            _logger.LogInformation(
                "Skipping pause for translation request {RequestId} because it is no longer the active job",
                request.Id);
            return false;
        }

        await _dbContext.Entry(request).ReloadAsync(cancellationToken);
        var translationRequest = request;

        if (requestLogs.Count > 0)
        {
            _dbContext.TranslationRequestLogs.AddRange(requestLogs);
        }

        _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = translationRequest.Id,
            Level = "Warning",
            Message = $"Translation paused: {reason}",
            Details = exception.ToString()
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, translationRequest.Progress);

        try
        {
            await RefreshTranslationStateAsync(translationRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state after pause");
        }

        _logger.LogWarning(
            exception,
            "Paused translation request {RequestId} for provider {Provider} until {ResumeAt:u}: {Reason}",
            translationRequest.Id,
            exception.Provider,
            resumeAt,
            reason);

        return true;
    }

    internal async Task<bool> HandleCompletion(
        TranslationRequest translationRequest,
        IReadOnlyCollection<string> outputPaths,
        CancellationToken cancellationToken)
    {
        var writtenOutput = new WrittenSubtitleOutput(
            translationRequest.TranslatedSubtitle ?? outputPaths.FirstOrDefault() ?? string.Empty,
            translationRequest.GeneratedOutputFormats ?? string.Empty,
            outputPaths,
            [],
            []);
        return await HandleCompletion(translationRequest, writtenOutput, cancellationToken);
    }

    private async Task<bool> HandleCompletion(
        TranslationRequest translationRequest,
        WrittenSubtitleOutput writtenOutput,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var translatedSubtitle = translationRequest.TranslatedSubtitle;
        var generatedOutputFormats = translationRequest.GeneratedOutputFormats;
        var generatedSubtitlePaths = translationRequest.GeneratedSubtitlePaths;

        var ownershipToken = translationRequest.JobId;
        var completionQuery = _dbContext.TranslationRequests
            .Where(item => item.Id == translationRequest.Id &&
                          item.Status == TranslationStatus.InProgress &&
                          item.JobId == ownershipToken);

        var publishedOutputs = new List<PublishedSubtitleArtifact>();
        EmbeddedPublicationTransaction? embeddedPublication = null;
        var completionCommitted = false;
        var commitAttempted = false;
        IAsyncDisposable? publicationLease = null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            publicationLease = await MkvEmbeddingService.AcquirePublicationLeaseAsync(
                GetPublicationPaths(writtenOutput),
                cancellationToken);

            var stillOwnsRequest = await completionQuery.AnyAsync(cancellationToken);
            if (!stillOwnsRequest)
            {
                await _dbContext.Entry(translationRequest).ReloadAsync(cancellationToken);
                CleanupStagedOutputArtifacts(
                    writtenOutput.StagedOutputs,
                    writtenOutput.StagedEmbeddedOutputs);
                _logger.LogInformation(
                    "Skipping completion for translation request {RequestId} because it is no longer active",
                    translationRequest.Id);
                return false;
            }

            PublishStagedOutputs(writtenOutput, publishedOutputs, translationRequest.Id, cancellationToken);
            embeddedPublication = await CreateEmbeddedPublicationTransactionAsync(
                writtenOutput,
                ownershipToken,
                translationRequest.Id,
                cancellationToken);
            await PublishStagedEmbeddedOutputsAsync(
                writtenOutput,
                embeddedPublication,
                cancellationToken);

            if (BeforeFinalCompletionCommitAsync != null)
            {
                await BeforeFinalCompletionCommitAsync();
            }

            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var rowsUpdated = await completionQuery
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.TranslatedSubtitle, translatedSubtitle)
                    .SetProperty(item => item.GeneratedOutputFormats, generatedOutputFormats)
                    .SetProperty(item => item.GeneratedSubtitlePaths, generatedSubtitlePaths)
                    .SetProperty(item => item.CompletedAt, now)
                    .SetProperty(item => item.Status, TranslationStatus.Completed)
                    .SetProperty(item => item.IsActive, (bool?)null)
                    .SetProperty(item => item.PausedAt, (DateTime?)null)
                    .SetProperty(item => item.PauseReason, (string?)null)
                    .SetProperty(item => item.PausedProvider, (string?)null)
                    .SetProperty(item => item.RetryCount, 0)
                    .SetProperty(item => item.NextRetryAt, (DateTime?)null)
                    .SetProperty(item => item.JobId, (string?)null)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);

            if (rowsUpdated == 0)
            {
                await transaction!.RollbackAsync(cancellationToken);
                await _dbContext.Entry(translationRequest).ReloadAsync(cancellationToken);
                RollbackPublishedOutputs(publishedOutputs);
                RollbackEmbeddedPublication(embeddedPublication);
                CleanupStagedOutputArtifacts(
                    writtenOutput.StagedOutputs,
                    writtenOutput.StagedEmbeddedOutputs);
                _logger.LogInformation(
                    "Skipping completion for translation request {RequestId} because it is no longer active",
                    translationRequest.Id);
                return false;
            }

            commitAttempted = true;
            await transaction!.CommitAsync(CancellationToken.None);
            completionCommitted = true;
            CleanupEmbeddedPublicationBackups(embeddedPublication);
            FinalizePublishedOutputs(writtenOutput, publishedOutputs);
        }
        catch
        {
            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogWarning(
                        rollbackException,
                        "Failed to roll back translation request state after publication failure for request {RequestId}",
                        translationRequest.Id);
                }
            }

            if (!completionCommitted && commitAttempted)
            {
                try
                {
                    completionCommitted = await _dbContext.TranslationRequests
                        .AsNoTracking()
                        .AnyAsync(item => item.Id == translationRequest.Id &&
                                          item.Status == TranslationStatus.Completed &&
                                          item.JobId == null &&
                                          item.TranslatedSubtitle == translatedSubtitle &&
                                          item.GeneratedSubtitlePaths == generatedSubtitlePaths,
                            CancellationToken.None);
                }
                catch (Exception probeException)
                {
                    _logger.LogWarning(
                        probeException,
                        "Could not determine whether translation completion committed for request {RequestId}",
                        translationRequest.Id);
                }
            }

            if (!completionCommitted)
            {
                RollbackPublishedOutputs(publishedOutputs);
                RollbackEmbeddedPublication(embeddedPublication);
            }
            else
            {
                CleanupEmbeddedPublicationBackups(embeddedPublication);
                CleanupPublicationBackups(publishedOutputs);
            }

            CleanupStagedOutputArtifacts(
                writtenOutput.StagedOutputs,
                writtenOutput.StagedEmbeddedOutputs);
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }

            if (publicationLease != null)
            {
                await publicationLease.DisposeAsync();
            }
        }

        await _dbContext.Entry(translationRequest).ReloadAsync(cancellationToken);

        if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
        {
            await _uploadWorkspaceService.HandleRequestCompletedAsync(
                translationRequest,
                writtenOutput.OutputPaths,
                cancellationToken);
        }

        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100);
        if (_translationCheckpointService != null)
        {
            await _translationCheckpointService.DeleteAsync(
                translationRequest.Id,
                cancellationToken,
                ownershipToken);
        }

        try
        {
            await RefreshEmbeddedSubtitleIndexAfterEmbeddedOutputAsync(
                translationRequest,
                writtenOutput.OutputPaths,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh embedded subtitle index after MKV embedding for request {RequestId}",
                translationRequest.Id);
        }

        try
        {
            await RefreshTranslationStateAsync(translationRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update translation state after completion");
        }

        return true;
    }

    private async Task RefreshEmbeddedSubtitleIndexAfterEmbeddedOutputAsync(
        TranslationRequest request,
        IReadOnlyCollection<string> outputPaths,
        CancellationToken cancellationToken)
    {
        if (request.WorkloadKind != TranslationWorkloadKind.Library ||
            !request.MediaId.HasValue ||
            !outputPaths.Any(path => path.StartsWith("mkv-embedded:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
            if (movie != null)
            {
                await _extractionService.SyncEmbeddedSubtitles(movie);
            }

            return;
        }

        var episode = await _dbContext.Episodes
            .FirstOrDefaultAsync(item => item.Id == request.MediaId.Value, cancellationToken);
        if (episode != null)
        {
            await _extractionService.SyncEmbeddedSubtitles(episode);
        }
    }

    internal async Task HandleCancellation(
        TranslationRequest request,
        string? ownershipToken = null)
    {
        ownershipToken = ResolveOwnershipToken(request, ownershipToken);
        var cleanupToken = CancellationToken.None;

        var translationRequest =
            await _dbContext.TranslationRequests.FirstOrDefaultAsync(translationRequest =>
                translationRequest.Id == request.Id, cleanupToken);

        if (translationRequest == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var expectedStatus = translationRequest.Status;
        var cleanupOwner = $"cancellation:{Guid.NewGuid():N}";
        var ownershipQuery = _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id &&
                           item.Status == expectedStatus &&
                           item.JobId == ownershipToken &&
                           (item.Status == TranslationStatus.Pending ||
                            item.Status == TranslationStatus.InProgress ||
                            item.Status == TranslationStatus.Paused));

        var rowsClaimed = await ownershipQuery
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, TranslationStatus.InProgress)
                .SetProperty(item => item.JobId, cleanupOwner)
                .SetProperty(item => item.UpdatedAt, now), cleanupToken);

        if (rowsClaimed == 0)
        {
            return;
        }

        await _dbContext.Entry(translationRequest).ReloadAsync(cleanupToken);
        await DeleteCheckpointSafelyAsync(
            translationRequest.Id,
            cleanupOwner,
            cleanupToken);

        var rowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id &&
                           item.Status == TranslationStatus.InProgress &&
                           item.JobId == cleanupOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CompletedAt, now)
                .SetProperty(item => item.Status, TranslationStatus.Cancelled)
                .SetProperty(item => item.IsActive, (bool?)null)
                .SetProperty(item => item.PausedAt, (DateTime?)null)
                .SetProperty(item => item.PauseReason, (string?)null)
                .SetProperty(item => item.PausedProvider, (string?)null)
                .SetProperty(item => item.NextRetryAt, (DateTime?)null)
                .SetProperty(item => item.JobId, (string?)null)
                .SetProperty(item => item.UpdatedAt, now), cleanupToken);

        if (rowsUpdated == 0)
        {
            return;
        }

        await _dbContext.Entry(translationRequest).ReloadAsync(cleanupToken);
        _logger.LogInformation("Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
            translationRequest.SubtitleToTranslate);
        if (translationRequest.WorkloadKind == TranslationWorkloadKind.Upload)
        {
            await _uploadWorkspaceService.HandleRequestCancelledAsync(translationRequest, cleanupToken);
        }

        await _translationRequestService.ClearMediaHash(translationRequest);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 0);
        await RefreshTranslationStateAsync(translationRequest, cleanupToken);
    }

    internal async Task<bool> TryCancelObsoleteUnsafeSourceAsync(
        TranslationRequest request,
        EmbeddedSubtitle? selectedSubtitle,
        IReadOnlyList<SubtitleItem> subtitles,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken,
        string? ownershipToken = null)
    {
        ownershipToken = ResolveOwnershipToken(request, ownershipToken);
        var reason = GetUnsafeSourceCancellationReason(
            request,
            selectedSubtitle,
            subtitles,
            settings);

        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        const string failureMessage =
            "Translation failed before execution because the selected source is obsolete or unsafe.";
        var now = DateTime.UtcNow;
        var cleanupOwner = $"unsafe-source:{Guid.NewGuid():N}";

        var unsafeSourceQuery = _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id &&
                          item.Status == TranslationStatus.InProgress &&
                          item.JobId == ownershipToken);

        var rowsClaimed = await unsafeSourceQuery
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.JobId, cleanupOwner)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (rowsClaimed == 0)
        {
            _logger.LogInformation(
                "Skipping obsolete unsafe source failure for translation request {RequestId} because it is no longer active",
                request.Id);
            return true;
        }

        await _dbContext.Entry(request).ReloadAsync(cancellationToken);
        await DeleteCheckpointSafelyAsync(request.Id, cleanupOwner, cancellationToken);

        var rowsUpdated = await _dbContext.TranslationRequests
            .Where(item => item.Id == request.Id &&
                          item.Status == TranslationStatus.InProgress &&
                          item.JobId == cleanupOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CompletedAt, (DateTime?)null)
                .SetProperty(item => item.Status, TranslationStatus.Failed)
                .SetProperty(item => item.FailedAt, now)
                .SetProperty(item => item.RetryCount, item => item.RetryCount + 1)
                .SetProperty(item => item.IsActive, (bool?)null)
                .SetProperty(item => item.PausedAt, (DateTime?)null)
                .SetProperty(item => item.PauseReason, (string?)null)
                .SetProperty(item => item.PausedProvider, (string?)null)
                .SetProperty(item => item.NextRetryAt, (DateTime?)null)
                .SetProperty(item => item.JobId, (string?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (rowsUpdated == 0)
        {
            _logger.LogInformation(
                "Skipping obsolete unsafe source failure for translation request {RequestId} because its cleanup ownership was lost",
                request.Id);
            return true;
        }

        await _dbContext.Entry(request).ReloadAsync(cancellationToken);
        _logger.LogError(
            "Marking obsolete unsafe translation request {RequestId} as failed: {Reason}",
            request.Id,
            reason);

        _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
        {
            TranslationRequestId = request.Id,
            Level = "Error",
            Message = failureMessage,
            Details = reason
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _translationRequestService.ClearMediaHash(request);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(request, 0);

        await RefreshTranslationStateAsync(request, cancellationToken);
        return true;
    }

    private async Task DeleteCheckpointSafelyAsync(
        int requestId,
        string? ownershipToken,
        CancellationToken cancellationToken)
    {
        if (_translationCheckpointService == null)
        {
            return;
        }

        try
        {
            await _translationCheckpointService.DeleteAsync(
                requestId,
                cancellationToken,
                ownershipToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete the translation checkpoint for request {RequestId} before releasing attempt ownership",
                requestId);
        }
    }

    private static string? ResolveOwnershipToken(
        TranslationRequest request,
        string? ownershipToken)
    {
        return string.IsNullOrWhiteSpace(ownershipToken)
            ? request.JobId
            : ownershipToken;
    }

    internal static string? GetUnsafeSourceCancellationReason(
        TranslationRequest request,
        EmbeddedSubtitle? selectedSubtitle,
        IReadOnlyList<SubtitleItem> subtitles,
        IReadOnlyDictionary<string, string> settings)
    {
        var supplementalEnabled = settings.TryGetValue(
                                      SettingKeys.Translation.TranslateSupplementalSubtitles,
                                      out var supplementalSetting) &&
                                  string.Equals(
                                      supplementalSetting,
                                      "true",
                                      StringComparison.OrdinalIgnoreCase);
        var isSupplementalSource =
            SubtitleLanguageHelper.IsSupplementalSubtitleType(request.SourceSubtitleType);

        if (isSupplementalSource && !supplementalEnabled)
        {
            return $"Selected source is {request.SourceSubtitleType}, but supplemental subtitle translation is disabled.";
        }

        if (request.SourceSubtitleEntryCount > 0 &&
            request.SourceSubtitleEntryCount < SubtitleExtractionService.MinimumDialogueEntries &&
            !(supplementalEnabled && isSupplementalSource))
        {
            return $"Selected source has only {request.SourceSubtitleEntryCount} entries; minimum full-dialogue threshold is {SubtitleExtractionService.MinimumDialogueEntries}.";
        }

        if (EmbeddedSourceLanguageMismatchesRequest(request, selectedSubtitle))
        {
            return $"Selected embedded source language '{selectedSubtitle!.Language}' does not match request source language '{request.SourceLanguage}'.";
        }

        return null;
    }

    internal static bool EmbeddedSourceLanguageMismatchesRequest(
        TranslationRequest request,
        EmbeddedSubtitle? selectedSubtitle)
    {
        if (selectedSubtitle == null ||
            string.IsNullOrWhiteSpace(selectedSubtitle.Language) ||
            string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            return false;
        }

        return !SubtitleLanguageHelper.LanguageMatches(
            selectedSubtitle.Language,
            request.SourceLanguage);
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

        if (translationRequest.WorkloadKind == TranslationWorkloadKind.Library &&
            _embeddedSubtitleCacheService.IsManagedCachePath(temporaryFilePath))
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

        var embeddedSubtitles = (await _extractionService.ProbeEmbeddedSubtitles(customItem.Path))
            .Where(subtitle => subtitle.IsTextBased)
            .ToList();
        var candidate = request.SourceSnapshotStreamIndex.HasValue
            ? embeddedSubtitles.FirstOrDefault(subtitle =>
                subtitle.StreamIndex == request.SourceSnapshotStreamIndex.Value)
            : null;

        if (candidate == null)
        {
            var ignoreCaptions = string.Equals(
                await _settings.GetSetting(SettingKeys.Translation.IgnoreCaptions),
                "true",
                StringComparison.OrdinalIgnoreCase);
            var selection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                embeddedSubtitles,
                [request.SourceLanguage],
                allowCaptionFallback: !ignoreCaptions,
                cancellationToken: cancellationToken);
            candidate = selection.SelectedSubtitle;
        }

        if (candidate == null)
        {
            return null;
        }

        return await _extractionService.ExtractSubtitle(
            customItem.Path,
            candidate.StreamIndex,
            outputDirectory,
            candidate.CodecName,
            candidate.Language);
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
        if (string.IsNullOrWhiteSpace(subtitlePath))
        {
            return false;
        }

        if (selectedSubtitle != null &&
            !string.IsNullOrWhiteSpace(selectedSubtitle.ExtractedPath) &&
            string.Equals(selectedSubtitle.ExtractedPath, subtitlePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (selectedSubtitle != null &&
            !string.IsNullOrWhiteSpace(selectedSubtitle.OcrExtractedPath) &&
            string.Equals(selectedSubtitle.OcrExtractedPath, subtitlePath, StringComparison.OrdinalIgnoreCase))
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
                    .IgnoreAutoIncludes()
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

            matched = embeddedSubtitles.FirstOrDefault(es =>
                !string.IsNullOrEmpty(es.OcrExtractedPath) &&
                es.OcrExtractedPath.Equals(subtitlePath, StringComparison.OrdinalIgnoreCase));

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

    internal async Task<TranslationPromptContext?> BuildOcrTranslationPromptContextAsync(
        TranslationRequest request,
        EmbeddedSubtitle? selectedSubtitle,
        CancellationToken cancellationToken)
    {
        var isOcrSource = selectedSubtitle?.HasUsableOcr() == true ||
                          IsOcrCachePath(request.SubtitleToTranslate);
        if (!isOcrSource)
        {
            return null;
        }

        var context = new TranslationPromptContext
        {
            IsOcrDerivedSource = true,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            SelectedStreamTitle = selectedSubtitle?.Title,
            SourceSubtitleType = request.SourceSubtitleType,
            SourceNote = BuildOcrSourceNote(selectedSubtitle)
        };

        if (request.MediaId.HasValue && request.MediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new { item.Title })
                .FirstOrDefaultAsync(cancellationToken);
            context.MovieTitle = movie?.Title ?? request.Title;
        }
        else if (request.MediaId.HasValue && request.MediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(item => item.Id == request.MediaId.Value)
                .Select(item => new
                {
                    item.EpisodeNumber,
                    item.Title,
                    SeasonNumber = item.Season.SeasonNumber,
                    SeriesTitle = item.Season.Show.Title
                })
                .FirstOrDefaultAsync(cancellationToken);
            context.SeriesTitle = episode?.SeriesTitle;
            context.SeasonNumber = episode?.SeasonNumber;
            context.EpisodeNumber = episode?.EpisodeNumber;
            context.EpisodeTitle = episode?.Title ?? request.Title;
        }
        else
        {
            context.MovieTitle = request.Title;
        }

        return context;
    }

    private static bool IsOcrCachePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return fileName.Contains(".ocr.", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".ocr.srt", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOcrSourceNote(EmbeddedSubtitle? selectedSubtitle)
    {
        return string.Equals(selectedSubtitle?.CodecName, "hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase)
            ? "OCR from Blu-ray PGS subtitles"
            : $"OCR from {selectedSubtitle?.CodecName ?? "image-based"} subtitles";
    }

}
