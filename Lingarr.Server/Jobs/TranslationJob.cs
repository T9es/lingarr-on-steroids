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
        ISubtitleExtractionService extractionService)
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
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("translation")]
    public async Task Execute(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        var jobId = JobContextFilter.GetCurrentJobId();

        // Acquire a parallel translation slot (blocks if limit reached)
        using var slot = await _parallelLimiter.AcquireAsync(cancellationToken);

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
            cancellationToken.ThrowIfCancellationRequested();

            var request = await _translationRequestService.UpdateTranslationRequest(translationRequest,
                TranslationStatus.InProgress,
                jobId);

            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                translationRequest.SubtitleToTranslate);
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
                subtitlePath = await TryExtractEmbeddedSubtitle(request);
                
                if (string.IsNullOrEmpty(subtitlePath))
                {
                    var errorMessage = $"Subtitle file not found and no extractable embedded subtitle available: {request.SubtitleToTranslate}";
                    _logger.LogError(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }
                
                // Update the request with the extracted subtitle path
                request.SubtitleToTranslate = subtitlePath;
                _logger.LogInformation("Using extracted embedded subtitle: {Path}", subtitlePath);
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
                    _logger.LogWarning("Subtitle is not valid according to configured preferences.");
                    throw new TaskCanceledException("Subtitle is not valid according to configured preferences.");
                }

                var isValid = _subtitleService.ValidateSubtitle(
                    request.SubtitleToTranslate,
                    validationOptions);

                if (!isValid)
                {
                    _logger.LogWarning("Subtitle is not valid according to configured preferences.");
                    throw new TaskCanceledException("Subtitle is not valid according to configured preferences.");
                }
            }

            // translate subtitles
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
            var translator = new SubtitleTranslationService(translationService, _logger, _progressService, _batchFallbackService);
            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
            
            // Parse batch fallback settings
            var enableBatchFallback = settings[SettingKeys.Translation.EnableBatchFallback] == "true";
            var maxBatchSplitAttempts = int.TryParse(settings[SettingKeys.Translation.MaxBatchSplitAttempts], out var splitAttempts)
                ? splitAttempts
                : 3;
            
            // Generate a short, readable identifier from the filename for logging
            // e.g., "S02E23" or "Movie Name (2024)"
            var fileIdentifier = GenerateFileIdentifier(translationRequest.SubtitleToTranslate);
            
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
                    await CleanSourceSubtitleFile(translationRequest.SubtitleToTranslate, stripSubtitleFormatting);
                    _logger.LogInformation("[{FileId}] Cleaned ASS drawing commands from source file", fileIdentifier);
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
                    "[{FileId}] Starting batch translation: {SubtitleCount} subtitles, {TotalBatches} batch(es) of {BatchSize}, fallback: {EnableFallback} ({SplitAttempts} attempts)",
                    fileIdentifier, subtitles.Count, totalBatches, effectiveBatchSize, enableBatchFallback, maxBatchSplitAttempts);

                translatedSubtitles = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    translationRequest,
                    stripSubtitleFormatting,
                    maxSize,
                    enableBatchFallback,
                    maxBatchSplitAttempts,
                    fileIdentifier,
                    cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "[{FileId}] Starting individual translation: {SubtitleCount} subtitles, context (before: {ContextBefore}, after: {ContextAfter})",
                    fileIdentifier, subtitles.Count, contextBefore, contextAfter);

                translatedSubtitles = await translator.TranslateSubtitles(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    contextBefore,
                    contextAfter,
                    cancellationToken
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

            if (stripSubtitleFormatting)
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
            await HandleCompletion(jobName, request, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            await HandleCancellation(jobName, translationRequest);
        }
        catch (Exception)
        {
            await _translationRequestService.ClearMediaHash(translationRequest);
            translationRequest = await _translationRequestService.UpdateTranslationRequest(translationRequest, TranslationStatus.Failed,
                jobId);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
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
    private static string GenerateFileIdentifier(string subtitlePath)
    {
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
    /// Prioritizes text-based subtitles that match the source language.
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
            EmbeddedSubtitle? bestCandidate = null;
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

                bestCandidate = FindBestEmbeddedSubtitle(episode.EmbeddedSubtitles, request.SourceLanguage);
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

                bestCandidate = FindBestEmbeddedSubtitle(movie.EmbeddedSubtitles, request.SourceLanguage);
                mediaPath = Path.Combine(movie.Path, movie.FileName);
                outputDir = movie.Path;
            }
            else
            {
                _logger.LogWarning("Unsupported media type for embedded extraction: {MediaType}", request.MediaType);
                return null;
            }

            if (bestCandidate == null)
            {
                _logger.LogInformation("No suitable embedded subtitle found for source language: {Language}", request.SourceLanguage);
                return null;
            }

            _logger.LogInformation(
                "Found embedded subtitle candidate: Stream {StreamIndex}, Language: {Language}, Codec: {Codec}",
                bestCandidate.StreamIndex, bestCandidate.Language ?? "unknown", bestCandidate.CodecName);

            // Extract the subtitle
            var extractedPath = await _extractionService.ExtractSubtitle(
                mediaPath!,
                bestCandidate.StreamIndex,
                outputDir!,
                bestCandidate.CodecName);

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
    /// Finds the best embedded subtitle candidate for translation.
    /// Prioritizes: text-based > matching source language > default > first available
    /// </summary>
    private static EmbeddedSubtitle? FindBestEmbeddedSubtitle(List<EmbeddedSubtitle>? embeddedSubtitles, string sourceLanguage)
    {
        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
            return null;

        // Only consider text-based subtitles
        var textBased = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
        if (textBased.Count == 0)
            return null;

        // Try to find one matching the source language
        var matchingLanguage = textBased.FirstOrDefault(s =>
            !string.IsNullOrEmpty(s.Language) &&
            s.Language.Equals(sourceLanguage, StringComparison.OrdinalIgnoreCase));

        if (matchingLanguage != null)
            return matchingLanguage;

        // Try default subtitle
        var defaultSub = textBased.FirstOrDefault(s => s.IsDefault);
        if (defaultSub != null)
            return defaultSub;

        // Return first text-based subtitle
        return textBased.First();
    }
}