using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Interfaces.Services;
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
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settings;
    private readonly ILogger<TranslateController> _logger;

    public TranslateController(
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        LingarrDbContext dbContext,
        ISettingService settings,
        ILogger<TranslateController> logger)
    {
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
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
        var serviceType = await _settings.GetSetting("service_type") ?? "localai";
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
                    translationsQueued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(movie, MediaType.Movie);
                    _logger.LogInformation("Movie {Title} queued {Count} translations", movie.Title, translationsQueued);
                    break;
                    
                case MediaType.Episode:
                    var episode = await _dbContext.Episodes.FindAsync(request.MediaId);
                    if (episode == null)
                        return NotFound(new TranslateMediaResponse { Message = "Episode not found" });
                    translationsQueued = await _mediaSubtitleProcessor.ProcessMediaForceAsync(episode, MediaType.Episode);
                    break;
                    
                case MediaType.Season:
                    var season = await _dbContext.Seasons
                        .Include(s => s.Episodes)
                        .FirstOrDefaultAsync(s => s.Id == request.MediaId);
                    if (season == null)
                        return NotFound(new TranslateMediaResponse { Message = "Season not found" });
                    foreach (var ep in season.Episodes.Where(e => !e.ExcludeFromTranslation))
                    {
                        translationsQueued += await _mediaSubtitleProcessor.ProcessMediaForceAsync(ep, MediaType.Episode);
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
                            var epCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(ep, MediaType.Episode);
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
}