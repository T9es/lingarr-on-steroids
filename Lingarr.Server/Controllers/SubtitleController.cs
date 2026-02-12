using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

public class SubtitlePath
{
    public required string  Path { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class SubtitleController : ControllerBase
{
    private readonly ISubtitleService _subtitleService;
    private readonly ISubtitleIntegrityService _integrityService;
    private readonly ISubtitleExtractionService _extractionService;

    public SubtitleController(
        ISubtitleService subtitleService,
        ISubtitleIntegrityService integrityService,
        ISubtitleExtractionService extractionService)
    {
        _subtitleService = subtitleService;
        _integrityService = integrityService;
        _extractionService = extractionService;
    }
    
    /// <summary>
    /// Retrieves a list of subtitle files located at the specified path.
    /// </summary>
    /// <param name="subtitlePath">The directory path to search for subtitle files.This path is relative to the media folder
    /// and should not start with a forward slash.</param>
    /// <returns>Returns an HTTP 200 OK response with a list of <see cref="Subtitles"/> objects found at the specified path.</returns>
    [HttpPost("all")]
    public async Task<ActionResult<List<Subtitles>>> GetAllSubtitles([FromBody] SubtitlePath subtitlePath)
    {
        var value = await _subtitleService.GetAllSubtitles(subtitlePath.Path);
        return Ok(value);
    }

    /// <summary>
    /// Scans all translated subtitle files for ASS drawing command artifacts.
    /// Used to detect files that may contain hallucinated vector drawing garbage.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Returns result containing list of flagged files</returns>
    [HttpPost("verify-ass")]
    public async Task<ActionResult<AssVerificationResult>> VerifyAssIntegrity(CancellationToken ct)
    {
        var result = await _integrityService.VerifyAssIntegrityAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Scans all completed translations for potentially incomplete source subtitles.
    /// Detects Forced or Signs-only subtitles that should be re-translated.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Returns summary of incomplete subtitle findings</returns>
    [HttpPost("validate-subtitle-types")]
    public async Task<ActionResult<SubtitleTypeCheckSummary>> ValidateSubtitleTypes(CancellationToken ct)
    {
        var result = await _integrityService.ValidateAllSubtitleTypesAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Validates a specific translation's subtitle type.
    /// </summary>
    /// <param name="translationId">The translation request ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Returns the validation result for the specified translation</returns>
    [HttpGet("validate-subtitle-type/{translationId}")]
    public async Task<ActionResult<SubtitleTypeCheckResult>> ValidateSubtitleType(int translationId, CancellationToken ct)
    {
        var result = await _integrityService.ValidateSubtitleTypeAsync(translationId, ct);
        
        if (result == null)
        {
            return NotFound($"Translation {translationId} not found or could not be validated");
        }

        return Ok(result);
    }

    /// <summary>
    /// Lists all available embedded subtitles for a movie or episode with metadata and entry counts.
    /// </summary>
    /// <param name="mediaType">The type of media ('movie' or 'episode')</param>
    /// <param name="mediaId">The media ID</param>
    /// <returns>List of available subtitles with metadata</returns>
    [HttpGet("available/{mediaType}/{mediaId:int}")]
    public async Task<ActionResult<List<AvailableSubtitleResponse>>> GetAvailableSubtitles(string mediaType, int mediaId)
    {
        if (!mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase) && 
            !mediaType.Equals("episode", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Error = "Media type must be 'movie' or 'episode'" });
        }

        var type = mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase) 
            ? MediaType.Movie 
            : MediaType.Episode;

        var subtitles = await _extractionService.ListAvailableSubtitlesAsync(mediaId, type);
        return Ok(subtitles);
    }
}