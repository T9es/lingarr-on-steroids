using Lingarr.Server.Interfaces.Services.Subtitle;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/language-detection")]
public class LanguageDetectionController : ControllerBase
{
    private readonly ISubtitleLanguageDetectionService _languageDetectionService;
    private readonly ILogger<LanguageDetectionController> _logger;

    public LanguageDetectionController(
        ISubtitleLanguageDetectionService languageDetectionService,
        ILogger<LanguageDetectionController> logger)
    {
        _languageDetectionService = languageDetectionService;
        _logger = logger;
    }

    [HttpPost("detect")]
    public async Task<IActionResult> DetectUnknownLanguages(
        [FromQuery] int? movieId = null,
        [FromQuery] int? episodeId = null)
    {
        if (!movieId.HasValue && !episodeId.HasValue)
        {
            return BadRequest(new { Error = "Either movieId or episodeId must be provided" });
        }

        try
        {
            var detected = await _languageDetectionService.DetectUnknownLanguagesAsync(
                movieId, episodeId);

            return Ok(new { Detected = detected });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { Error = "Operation was cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting unknown languages for MovieId={MovieId}, EpisodeId={EpisodeId}",
                movieId, episodeId);
            return StatusCode(500, new { Error = "Language detection failed" });
        }
    }
}