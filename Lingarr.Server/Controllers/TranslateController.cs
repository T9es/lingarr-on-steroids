using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.Batch.Request;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Services;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranslateController : ControllerBase
{
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ILogger<TranslateController> _logger;

    public TranslateController(
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ISubtitleExtractionService extractionService,
        LingarrDbContext dbContext,
        ISettingService settings,
        ILogger<TranslateController> logger)
    {
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _extractionService = extractionService;
        _dbContext = dbContext;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Initiates a translation job for the provided subtitle data.
    /// </summary>
    /// <param name="translateAbleSubtitle">The subtitle data to be translated. 
    /// This includes the subtitle path, subtitle source language and subtitle target language.</param>
    /// <returns>Returns an HTTP 200 OK response if the job was successfully enqueued.</returns>
    [HttpPost("file")]
    public async Task<ActionResult<TranslationJobDto>> Translate([FromBody] TranslateAbleSubtitle translateAbleSubtitle)
    {
        var jobId = await _translationRequestService.CreateRequest(translateAbleSubtitle);
        return Ok(new TranslationJobDto
        {
            JobId = jobId,
        });
    }

    /// <summary>
    /// Translate a single subtitle line
    /// </summary>
    /// <param name="translateAbleSubtitleLine">The subtitle to be translated. 
    /// This includes the subtitle line, subtitle source language and subtitle target language.</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Returns translated string if the translation was successful.</returns>
    [HttpPost("line")]
    public async Task<string> TranslateLine(
        [FromBody] TranslateAbleSubtitleLine translateAbleSubtitleLine,
        CancellationToken cancellationToken)
    {
        var serviceType = await _settings.GetSetting(SettingKeys.Translation.ServiceType) ?? "localai";

        var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
        var subtitleTranslator = new SubtitleTranslationService(translationService, _logger);

        if (translateAbleSubtitleLine.SubtitleLine == "")
        {
            return translateAbleSubtitleLine.SubtitleLine;
        }
        return await subtitleTranslator.TranslateSubtitleLine(translateAbleSubtitleLine, cancellationToken);
    }

    /// <summary>
    /// Translates subtitle content, supporting both single line and batch translation.
    /// </summary>
    /// <param name="translateAbleSubtitleContent">The translation request containing one or more subtitle items</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Translated subtitle content</returns>
    [HttpPost("content")]
    public async Task<ActionResult<BatchTranslatedLine[]>> TranslateContent(
        [FromBody] TranslateAbleSubtitleContent translateAbleSubtitleContent,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await _translationRequestService.TranslateContentAsync(translateAbleSubtitleContent, cancellationToken);
            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves a list of available source languages and their supported target languages.
    /// </summary>
    /// <returns>A list of source languages, each containing its code, name, and list of supported target language codes</returns>
    /// <exception cref="InvalidOperationException">Thrown when service is not properly configured or initialization fails</exception>
    /// <exception cref="JsonException">Thrown when language configuration files cannot be parsed (for file-based services)</exception>
    [HttpGet("languages")]
    public async Task<List<SourceLanguage>> GetLanguages()
    {
        var serviceType = await _settings.GetSetting("service_type");
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            serviceType = "localai";
        }
        var translationService = _translationServiceFactory.CreateTranslationService(serviceType);

        return await translationService.GetLanguages();
    }

    /// <summary>
    /// Retrieves available AI models for the currently active translation service.
    /// </summary>
    /// <returns>A list of models in a standardized label/value format for frontend consumption</returns>
    /// <exception cref="InvalidOperationException">Thrown when service is not properly configured or initialization fails</exception>
    [HttpGet("models")]
    public async Task<ActionResult<List<LabelValue>>> GetModels()
    {
        try
        {
            var serviceType = await _settings.GetSetting(SettingKeys.Translation.ServiceType) ?? "localai";
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);

            // Service-specific logic to get models
            var models = await translationService.GetModels();
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving models for translation service");
            return StatusCode(500, "Failed to retrieve available models");
        }
    }

    /// <summary>
    /// Manually triggers translation for a specific media item.
    /// </summary>
    /// <param name="request">The media item to translate (MediaId and MediaType).</param>
    /// <returns>The number of translations that were queued.</returns>
    [HttpPost("media")]
    public async Task<ActionResult<TranslateMediaResponse>> TranslateMedia([FromBody] TranslateMediaRequest request)
    {
        try
        {
            _logger.LogInformation(
                "TranslateMedia request received: MediaId={MediaId}, MediaType={MediaType}",
                request.MediaId, request.MediaType);
                
            var translationsQueued = 0;
            
            switch (request.MediaType)
            {
                case MediaType.Movie:
                    var movie = await _dbContext.Movies.FindAsync(request.MediaId);
                    if (movie == null)
                        return NotFound(new TranslateMediaResponse { Message = "Movie not found" });
                    _logger.LogInformation("Processing movie: {Title}, Path: {Path}", movie.Title, movie.Path);
                    translationsQueued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true, forcePriority: true);
                    _logger.LogInformation("Movie {Title} queued {Count} translations", movie.Title, translationsQueued);
                    break;
                    
                case MediaType.Episode:
                    var episode = await _dbContext.Episodes.FindAsync(request.MediaId);
                    if (episode == null)
                        return NotFound(new TranslateMediaResponse { Message = "Episode not found" });
                    translationsQueued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(episode, MediaType.Episode, forceProcess: true, forcePriority: true);
                    break;
                    
                case MediaType.Season:
                    var season = await _dbContext.Seasons
                        .Include(s => s.Episodes)
                        .FirstOrDefaultAsync(s => s.Id == request.MediaId);
                    if (season == null)
                        return NotFound(new TranslateMediaResponse { Message = "Season not found" });
                    foreach (var ep in season.Episodes.Where(e => !e.ExcludeFromTranslation))
                    {
                        translationsQueued += await _mediaSubtitleProcessor.ProcessMediaForceAsync(ep, MediaType.Episode, forceProcess: true, forcePriority: true);
                    }
                    break;
                    
                case MediaType.Show:
                    var show = await _dbContext.Shows
                        .Include(s => s.Seasons)
                        .ThenInclude(s => s.Episodes)
                        .FirstOrDefaultAsync(s => s.Id == request.MediaId);
                    if (show == null)
                        return NotFound(new TranslateMediaResponse { Message = "Show not found" });
                    _logger.LogInformation("Processing show: {Title} with {SeasonCount} seasons", 
                        show.Title, show.Seasons.Count);
                    var totalEpisodes = 0;
                    var excludedEpisodes = 0;
                    foreach (var s in show.Seasons.Where(s => !s.ExcludeFromTranslation))
                    {
                        foreach (var ep in s.Episodes.Where(e => !e.ExcludeFromTranslation))
                        {
                            totalEpisodes++;
                            var epCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(ep, MediaType.Episode, forceProcess: true, forcePriority: true);
                            translationsQueued += epCount;
                            if (epCount == 0)
                            {
                                _logger.LogDebug("Episode S{Season}E{Episode} ({Title}) queued 0 translations",
                                    s.SeasonNumber, ep.EpisodeNumber, ep.Title);
                            }
                        }
                        excludedEpisodes += s.Episodes.Count(e => e.ExcludeFromTranslation);
                    }
                    _logger.LogInformation(
                        "Show {Title}: processed {Total} episodes, {Excluded} excluded, queued {Count} translations",
                        show.Title, totalEpisodes, excludedEpisodes, translationsQueued);
                    break;
                    
                default:
                    return BadRequest(new TranslateMediaResponse { Message = "Invalid media type" });
            }

            var message = translationsQueued > 0 
                ? $"{translationsQueued} translation(s) queued" 
                : "No translations needed";
                
            return Ok(new TranslateMediaResponse 
            { 
                TranslationsQueued = translationsQueued,
                Message = message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating media {MediaId} of type {MediaType}", request.MediaId, request.MediaType);
            return StatusCode(500, new TranslateMediaResponse { Message = "Failed to queue translations" });
        }
    }

    /// <summary>
    /// Queues translation jobs using a specific embedded subtitle stream.
    /// </summary>
    /// <param name="request">The request containing media info and stream index</param>
    /// <returns>Result of the queuing operation</returns>
    [HttpPost("queue-with-subtitle")]
    public async Task<ActionResult<QueueWithSubtitleResponse>> QueueWithSubtitle([FromBody] QueueWithSubtitleRequest request)
    {
        try
        {
            _logger.LogInformation(
                "QueueWithSubtitle request received: MediaId={MediaId}, MediaType={MediaType}, StreamIndex={StreamIndex}",
                request.MediaId, request.MediaType, request.StreamIndex);

            // Parse media type
            if (!Enum.TryParse<MediaType>(request.MediaType, true, out var mediaType))
            {
                return BadRequest(new QueueWithSubtitleResponse 
                { 
                    Success = false, 
                    Message = "Invalid media type. Must be 'Movie' or 'Episode'." 
                });
            }

            // Get target languages from settings
            var targetLanguageModels = await _settings.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
            var targetLanguages = targetLanguageModels
                .Select(lang => lang.Code.ToLowerInvariant())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToList();

            if (targetLanguages.Count == 0)
            {
                return BadRequest(new QueueWithSubtitleResponse 
                { 
                    Success = false, 
                    Message = "No target languages configured. Please configure target languages in settings." 
                });
            }

            // Get available subtitles to validate the stream index
            var availableSubtitles = await _extractionService.ListAvailableSubtitlesAsync(request.MediaId, mediaType);
            var selectedSubtitle = availableSubtitles.FirstOrDefault(s => s.StreamIndex == request.StreamIndex);

            if (selectedSubtitle == null)
            {
                return NotFound(new QueueWithSubtitleResponse 
                { 
                    Success = false, 
                    Message = $"Subtitle stream with index {request.StreamIndex} not found for this media." 
                });
            }

            if (!selectedSubtitle.IsTextBased)
            {
                return BadRequest(new QueueWithSubtitleResponse 
                { 
                    Success = false, 
                    Message = "Cannot use image-based subtitles (PGS/VobSub). Please select a text-based subtitle." 
                });
            }

            var sourceLanguage = request.SourceLanguage.ToLowerInvariant();
            var translationsQueued = 0;

            // Queue translations for each target language
            foreach (var targetLanguage in targetLanguages)
            {
                // Check for existing active request
                var hasActiveRequest = await _dbContext.TranslationRequests.AnyAsync(tr =>
                    tr.MediaId == request.MediaId &&
                    tr.MediaType == mediaType &&
                    tr.SourceLanguage == sourceLanguage &&
                    tr.TargetLanguage == targetLanguage &&
                    (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress));

                if (hasActiveRequest)
                {
                    _logger.LogInformation(
                        "Skipping enqueue for MediaId={MediaId} {Source}->{Target}: translation request already active.",
                        request.MediaId, sourceLanguage, targetLanguage);
                    continue;
                }

                await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                {
                    MediaId = request.MediaId,
                    MediaType = mediaType,
                    SubtitlePath = null, // Will trigger extraction with specific stream index in TranslationJob
                    TargetLanguage = targetLanguage,
                    SourceLanguage = sourceLanguage,
                    SubtitleFormat = null
                }, forcePriority: true);

                translationsQueued++;
                _logger.LogInformation(
                    "Queued translation from |Orange|{Source}|/Orange| to |Orange|{Target}|/Orange| for MediaId={MediaId} using stream {StreamIndex}",
                    sourceLanguage, targetLanguage, request.MediaId, request.StreamIndex);
            }

            // Store the selected stream index for the job to use
            // We'll use a setting to pass this info to the translation job
            var streamSelectionKey = $"subtitle_stream_selection_{request.MediaId}_{mediaType}";
            await _settings.SetSetting(streamSelectionKey, request.StreamIndex.ToString());

            var message = translationsQueued > 0
                ? $"{translationsQueued} translation(s) queued using subtitle stream {request.StreamIndex}"
                : "No new translations needed (may already be in queue)";

            return Ok(new QueueWithSubtitleResponse
            {
                Success = true,
                Message = message,
                TranslationsQueued = translationsQueued
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing translation with specific subtitle for MediaId={MediaId}", request.MediaId);
            return StatusCode(500, new QueueWithSubtitleResponse 
            { 
                Success = false, 
                Message = $"Failed to queue translations: {ex.Message}" 
            });
        }
    }
}