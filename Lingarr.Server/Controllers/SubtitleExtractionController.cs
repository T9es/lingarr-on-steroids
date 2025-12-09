using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/subtitle")]
public class SubtitleExtractionController : ControllerBase
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ILogger<SubtitleExtractionController> _logger;

    public SubtitleExtractionController(
        LingarrDbContext dbContext,
        ISubtitleExtractionService extractionService,
        ILogger<SubtitleExtractionController> logger)
    {
        _dbContext = dbContext;
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <summary>
    /// Get all embedded subtitles for a movie
    /// </summary>
    /// <param name="id">Movie ID</param>
    /// <returns>List of embedded subtitles</returns>
    [HttpGet("movie/{id:int}/embedded")]
    public async Task<ActionResult<List<EmbeddedSubtitleResponse>>> GetMovieEmbeddedSubtitles(int id)
    {
        var movie = await _dbContext.Movies
            .Include(m => m.EmbeddedSubtitles)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie == null)
        {
            return NotFound(new { Error = "Movie not found" });
        }

        // If no embedded subtitles cached, probe the file now
        if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
        {
            await _extractionService.SyncEmbeddedSubtitles(movie);
            await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles!).LoadAsync();
        }

        var response = (movie.EmbeddedSubtitles ?? new List<EmbeddedSubtitle>())
            .Select(MapToResponse)
            .ToList();

        return Ok(response);
    }

    /// <summary>
    /// Get all embedded subtitles for an episode
    /// </summary>
    /// <param name="id">Episode ID</param>
    /// <returns>List of embedded subtitles</returns>
    [HttpGet("episode/{id:int}/embedded")]
    public async Task<ActionResult<List<EmbeddedSubtitleResponse>>> GetEpisodeEmbeddedSubtitles(int id)
    {
        var episode = await _dbContext.Episodes
            .Include(e => e.EmbeddedSubtitles)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (episode == null)
        {
            return NotFound(new { Error = "Episode not found" });
        }

        // If no embedded subtitles cached, probe the file now
        if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
        {
            await _extractionService.SyncEmbeddedSubtitles(episode);
            await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles!).LoadAsync();
        }

        var response = (episode.EmbeddedSubtitles ?? new List<EmbeddedSubtitle>())
            .Select(MapToResponse)
            .ToList();

        return Ok(response);
    }

    /// <summary>
    /// Extract a specific embedded subtitle from a movie
    /// </summary>
    /// <param name="id">Movie ID</param>
    /// <param name="streamIndex">FFmpeg stream index</param>
    /// <returns>Extraction result with file path</returns>
    [HttpPost("movie/{id:int}/extract/{streamIndex:int}")]
    public async Task<ActionResult<ExtractSubtitleResponse>> ExtractMovieSubtitle(int id, int streamIndex)
    {
        var movie = await _dbContext.Movies
            .Include(m => m.EmbeddedSubtitles)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie == null)
        {
            return NotFound(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Movie not found"
            });
        }

        var embeddedSub = movie.EmbeddedSubtitles?.FirstOrDefault(s => s.StreamIndex == streamIndex);
        if (embeddedSub == null)
        {
            return NotFound(new ExtractSubtitleResponse
            {
                Success = false,
                Error = $"Embedded subtitle with stream index {streamIndex} not found"
            });
        }

        if (!embeddedSub.IsTextBased)
        {
            return BadRequest(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Cannot extract image-based subtitles (PGS/VobSub). OCR is not supported."
            });
        }

        var mediaPath = Path.Combine(movie.Path, movie.FileName);
        var outputDir = movie.Path;

        return await ExtractAndUpdateAsync(embeddedSub, mediaPath, outputDir);
    }

    /// <summary>
    /// Extract a specific embedded subtitle from an episode
    /// </summary>
    /// <param name="id">Episode ID</param>
    /// <param name="streamIndex">FFmpeg stream index</param>
    /// <returns>Extraction result with file path</returns>
    [HttpPost("episode/{id:int}/extract/{streamIndex:int}")]
    public async Task<ActionResult<ExtractSubtitleResponse>> ExtractEpisodeSubtitle(int id, int streamIndex)
    {
        var episode = await _dbContext.Episodes
            .Include(e => e.EmbeddedSubtitles)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (episode == null)
        {
            return NotFound(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Episode not found"
            });
        }

        if (string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
        {
            return BadRequest(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Episode has no file path"
            });
        }

        var embeddedSub = episode.EmbeddedSubtitles?.FirstOrDefault(s => s.StreamIndex == streamIndex);
        if (embeddedSub == null)
        {
            return NotFound(new ExtractSubtitleResponse
            {
                Success = false,
                Error = $"Embedded subtitle with stream index {streamIndex} not found"
            });
        }

        if (!embeddedSub.IsTextBased)
        {
            return BadRequest(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Cannot extract image-based subtitles (PGS/VobSub). OCR is not supported."
            });
        }

        var mediaPath = Path.Combine(episode.Path, episode.FileName);
        var outputDir = episode.Path;

        return await ExtractAndUpdateAsync(embeddedSub, mediaPath, outputDir);
    }

    /// <summary>
    /// Force re-probe embedded subtitles for a movie
    /// </summary>
    [HttpPost("movie/{id:int}/probe")]
    public async Task<ActionResult<List<EmbeddedSubtitleResponse>>> ProbeMovieSubtitles(int id)
    {
        var movie = await _dbContext.Movies.FindAsync(id);
        if (movie == null)
        {
            return NotFound(new { Error = "Movie not found" });
        }

        await _extractionService.SyncEmbeddedSubtitles(movie);
        
        var embeddedSubs = await _dbContext.EmbeddedSubtitles
            .Where(e => e.MovieId == id)
            .ToListAsync();

        return Ok(embeddedSubs.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Force re-probe embedded subtitles for an episode
    /// </summary>
    [HttpPost("episode/{id:int}/probe")]
    public async Task<ActionResult<List<EmbeddedSubtitleResponse>>> ProbeEpisodeSubtitles(int id)
    {
        var episode = await _dbContext.Episodes.FindAsync(id);
        if (episode == null)
        {
            return NotFound(new { Error = "Episode not found" });
        }

        await _extractionService.SyncEmbeddedSubtitles(episode);
        
        var embeddedSubs = await _dbContext.EmbeddedSubtitles
            .Where(e => e.EpisodeId == id)
            .ToListAsync();

        return Ok(embeddedSubs.Select(MapToResponse).ToList());
    }

    private async Task<ActionResult<ExtractSubtitleResponse>> ExtractAndUpdateAsync(
        EmbeddedSubtitle embeddedSub,
        string mediaPath,
        string outputDir)
    {
        try
        {
            _logger.LogInformation(
                "Extracting embedded subtitle stream {StreamIndex} from {MediaPath}",
                embeddedSub.StreamIndex, Path.GetFileName(mediaPath));

            var extractedPath = await _extractionService.ExtractSubtitle(
                mediaPath,
                embeddedSub.StreamIndex,
                outputDir,
                embeddedSub.CodecName);

            if (string.IsNullOrEmpty(extractedPath))
            {
                return StatusCode(500, new ExtractSubtitleResponse
                {
                    Success = false,
                    Error = "Extraction failed. Check server logs for details."
                });
            }

            // Update the database record
            embeddedSub.IsExtracted = true;
            embeddedSub.ExtractedPath = extractedPath;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Successfully extracted subtitle to {ExtractedPath}",
                extractedPath);

            return Ok(new ExtractSubtitleResponse
            {
                Success = true,
                ExtractedPath = extractedPath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting subtitle stream {StreamIndex}", embeddedSub.StreamIndex);
            return StatusCode(500, new ExtractSubtitleResponse
            {
                Success = false,
                Error = $"Extraction failed: {ex.Message}"
            });
        }
    }

    private static EmbeddedSubtitleResponse MapToResponse(EmbeddedSubtitle entity) => new()
    {
        Id = entity.Id,
        StreamIndex = entity.StreamIndex,
        Language = entity.Language,
        Title = entity.Title,
        CodecName = entity.CodecName,
        IsTextBased = entity.IsTextBased,
        IsDefault = entity.IsDefault,
        IsForced = entity.IsForced,
        IsExtracted = entity.IsExtracted,
        ExtractedPath = entity.ExtractedPath
    };
}
