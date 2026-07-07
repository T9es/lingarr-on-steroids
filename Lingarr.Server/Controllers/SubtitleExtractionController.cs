using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Hangfire;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/subtitle")]
public class SubtitleExtractionController : ControllerBase
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ISubtitleOcrService _subtitleOcrService;
    private readonly IMediaStateService _mediaStateService;
    private readonly ILogger<SubtitleExtractionController> _logger;

    public SubtitleExtractionController(
        LingarrDbContext dbContext,
        ISubtitleExtractionService extractionService,
        ISubtitleOcrService subtitleOcrService,
        IMediaStateService mediaStateService,
        ILogger<SubtitleExtractionController> logger)
    {
        _dbContext = dbContext;
        _extractionService = extractionService;
        _subtitleOcrService = subtitleOcrService;
        _mediaStateService = mediaStateService;
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

        await NormalizeStaleExtractedSubtitlesAsync(movie.EmbeddedSubtitles);

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

        await NormalizeStaleExtractedSubtitlesAsync(episode.EmbeddedSubtitles);

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
                Error = "Cannot extract image-based subtitles directly. Use OCR for supported bitmap streams."
            });
        }

        if (string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
        {
            return BadRequest(new ExtractSubtitleResponse
            {
                Success = false,
                Error = "Movie has no file path"
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
                Error = "Cannot extract image-based subtitles directly. Use OCR for supported bitmap streams."
            });
        }

        var mediaPath = Path.Combine(episode.Path, episode.FileName);
        var outputDir = episode.Path;

        return await ExtractAndUpdateAsync(embeddedSub, mediaPath, outputDir);
    }

    [HttpPost("movie/{id:int}/ocr/{streamIndex:int}")]
    public async Task<ActionResult<SubtitleOcrResponse>> QueueMovieOcr(int id, int streamIndex)
    {
        return await QueueOcrAsync(id, MediaType.Movie, streamIndex, manual: true);
    }

    [HttpPost("episode/{id:int}/ocr/{streamIndex:int}")]
    public async Task<ActionResult<SubtitleOcrResponse>> QueueEpisodeOcr(int id, int streamIndex)
    {
        return await QueueOcrAsync(id, MediaType.Episode, streamIndex, manual: true);
    }

    [HttpPost("movie/{id:int}/ocr/{streamIndex:int}/approve")]
    public async Task<ActionResult<SubtitleOcrResponse>> ApproveMovieOcr(int id, int streamIndex)
    {
        var result = await _subtitleOcrService.ApproveOcrAsync(id, MediaType.Movie, streamIndex);
        return result.Success ? Ok(SubtitleOcrResponse.FromResult(result)) : BadRequest(SubtitleOcrResponse.FromResult(result));
    }

    [HttpPost("episode/{id:int}/ocr/{streamIndex:int}/approve")]
    public async Task<ActionResult<SubtitleOcrResponse>> ApproveEpisodeOcr(int id, int streamIndex)
    {
        var result = await _subtitleOcrService.ApproveOcrAsync(id, MediaType.Episode, streamIndex);
        return result.Success ? Ok(SubtitleOcrResponse.FromResult(result)) : BadRequest(SubtitleOcrResponse.FromResult(result));
    }

    [HttpGet("movie/{id:int}/ocr/{streamIndex:int}/preview")]
    public async Task<ActionResult> PreviewMovieOcr(int id, int streamIndex)
    {
        var result = await _subtitleOcrService.GetPreviewAsync(id, MediaType.Movie, streamIndex);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("episode/{id:int}/ocr/{streamIndex:int}/preview")]
    public async Task<ActionResult> PreviewEpisodeOcr(int id, int streamIndex)
    {
        var result = await _subtitleOcrService.GetPreviewAsync(id, MediaType.Episode, streamIndex);
        return result.Success ? Ok(result) : BadRequest(result);
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
        movie.IndexedAt = DateTime.UtcNow;
        
        await _mediaStateService.UpdateStateAsync(movie, MediaType.Movie);
        
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
        episode.IndexedAt = DateTime.UtcNow;
        
        await _mediaStateService.UpdateStateAsync(episode, MediaType.Episode);
        
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
                embeddedSub.CodecName,
                embeddedSub.Language);

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

    private async Task<ActionResult<SubtitleOcrResponse>> QueueOcrAsync(
        int id,
        MediaType mediaType,
        int streamIndex,
        bool manual)
    {
        var result = await _subtitleOcrService.QueueOcrAsync(id, mediaType, streamIndex, manual);
        if (!result.Success)
        {
            return BadRequest(SubtitleOcrResponse.FromResult(result));
        }

        if (result.Status == SubtitleOcrStatus.Queued)
        {
            BackgroundJob.Enqueue<SubtitleOcrJob>(job => job.Execute(id, mediaType, streamIndex, manual));
        }

        return Ok(SubtitleOcrResponse.FromResult(result));
    }

    private async Task NormalizeStaleExtractedSubtitlesAsync(List<EmbeddedSubtitle>? subtitles)
    {
        if (subtitles == null || subtitles.Count == 0)
        {
            return;
        }

        var staleSubtitles = subtitles
            .Where(sub => sub.IsExtracted &&
                          !string.IsNullOrEmpty(sub.ExtractedPath) &&
                          !System.IO.File.Exists(sub.ExtractedPath))
            .ToList();

        if (staleSubtitles.Count == 0)
        {
            return;
        }

        foreach (var staleSubtitle in staleSubtitles)
        {
            staleSubtitle.IsExtracted = false;
            staleSubtitle.ExtractedPath = null;
        }

        await _dbContext.SaveChangesAsync();
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
        ExtractedPath = entity.ExtractedPath,
        OcrStatus = entity.OcrStatus,
        OcrExtractedPath = entity.OcrExtractedPath,
        OcrError = entity.OcrError,
        OcrAttemptedAt = entity.OcrAttemptedAt,
        OcrCompletedAt = entity.OcrCompletedAt,
        OcrCueCount = entity.OcrCueCount,
        OcrQualityScore = entity.OcrQualityScore,
        OcrIssueSummary = entity.OcrIssueSummary,
        OcrApprovedAt = entity.OcrApprovedAt,
        IsOcrSupported = entity.IsTextBased == false &&
                         (string.Equals(entity.CodecName, "hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(entity.CodecName, "pgssub", StringComparison.OrdinalIgnoreCase)),
        IsOcrUsable = entity.HasUsableOcr()
    };
}
