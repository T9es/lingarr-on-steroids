using System.Text.Json;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Controllers;

/// <summary>
/// Controller for test translations with real-time logging.
/// Test translations do NOT save the result - they are for debugging only.
/// </summary>
[ApiController]
[Route("api/test-translation")]
public class TestTranslationController : ControllerBase
{
    private readonly ITestTranslationService _testTranslationService;
    private readonly LingarrDbContext _dbContext;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<TestTranslationController> _logger;
    
    public TestTranslationController(
        ITestTranslationService testTranslationService,
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ILogger<TestTranslationController> logger)
    {
        _testTranslationService = testTranslationService;
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get current test status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<object> GetStatus()
    {
        return Ok(new { IsRunning = _testTranslationService.IsRunning });
    }

    /// <summary>
    /// Fuzzy-search movies and episodes to help users pick a subtitle file
    /// for test translations without manually typing full paths.
    /// </summary>
    /// <param name="query">Free-text search query (movie/show/episode title, etc.)</param>
    /// <param name="limit">Maximum number of media results to return</param>
    [HttpGet("search")]
    public async Task<ActionResult<List<TestTranslationSearchResult>>> Search(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new List<TestTranslationSearchResult>());
        }

        var normalized = query.Trim().ToLowerInvariant();
        limit = Math.Clamp(limit, 1, 50);

        var results = new List<TestTranslationSearchResult>();

        try
        {
            // Movies
            var movieQuery = _dbContext.Movies.AsQueryable();

            movieQuery = movieQuery.Where(m =>
                m.Title.ToLower().Contains(normalized) ||
                (m.FileName != null && m.FileName.ToLower().Contains(normalized)));

            var movies = await movieQuery
                .OrderByDescending(m => m.DateAdded)
                .Take(limit)
                .ToListAsync(cancellationToken);

            foreach (var movie in movies)
            {
                if (string.IsNullOrEmpty(movie.Path))
                {
                    continue;
                }

                var subtitles = await _subtitleService.GetAllSubtitles(movie.Path);
                if (subtitles.Count == 0)
                {
                    continue;
                }

                results.Add(new TestTranslationSearchResult
                {
                    DisplayTitle = movie.Title,
                    MediaType = MediaType.Movie,
                    MediaId = movie.Id,
                    Subtitles = subtitles
                });

                if (results.Count >= limit)
                {
                    return Ok(results);
                }
            }

            // Episodes
            var episodeQuery = _dbContext.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .AsQueryable();

            episodeQuery = episodeQuery.Where(e =>
                e.Season.Show.Title.ToLower().Contains(normalized) ||
                e.Title.ToLower().Contains(normalized));

            var episodes = await episodeQuery
                .OrderByDescending(e => e.DateAdded ?? DateTime.MinValue)
                .Take(limit)
                .ToListAsync(cancellationToken);

            foreach (var episode in episodes)
            {
                var basePath = episode.Path ?? episode.Season.Path;
                if (string.IsNullOrEmpty(basePath))
                {
                    continue;
                }

                var subtitles = await _subtitleService.GetAllSubtitles(basePath);
                if (subtitles.Count == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(episode.FileName))
                {
                    var fileName = episode.FileName.ToLowerInvariant();
                    subtitles = subtitles
                        .Where(s => s.FileName.ToLowerInvariant().Contains(fileName))
                        .ToList();

                    if (subtitles.Count == 0)
                    {
                        continue;
                    }
                }

                var displayTitle =
                    $"{episode.Season.Show.Title} - S{episode.Season.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episode.Title}";

                results.Add(new TestTranslationSearchResult
                {
                    DisplayTitle = displayTitle,
                    MediaType = MediaType.Episode,
                    MediaId = episode.Id,
                    Subtitles = subtitles
                });

                if (results.Count >= limit)
                {
                    break;
                }
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error searching media for test translation with query {Query}",
                query);
            return StatusCode(500, "Failed to search media for test translation.");
        }
    }
    
    /// <summary>
    /// Start a test translation with real-time log streaming via SSE.
    /// </summary>
    [HttpPost("start")]
    public async Task StartTest([FromBody] TestTranslationRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        
        async void OnLogEntry(object? sender, TestTranslationLogEntry entry)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    type = "log",
                    entry.Level,
                    entry.Message,
                    entry.Timestamp,
                    entry.Details
                });
                
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to write log entry to SSE stream: {Error}", ex.Message);
            }
        }
        
        _testTranslationService.OnLogEntry += OnLogEntry;
        
        try
        {
            var result = await _testTranslationService.RunTestAsync(request, cancellationToken);
            
            // Send final result
            var resultJson = JsonSerializer.Serialize(new
            {
                type = "result",
                result.Success,
                result.ErrorMessage,
                result.TotalSubtitles,
                result.TranslatedCount,
                Duration = result.Duration.TotalSeconds,
                result.Preview
            });
            
            await Response.WriteAsync($"data: {resultJson}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _testTranslationService.OnLogEntry -= OnLogEntry;
        }
    }
    
    /// <summary>
    /// Cancel any in-progress test translation.
    /// </summary>
    [HttpPost("cancel")]
    public ActionResult Cancel()
    {
        _testTranslationService.CancelTest();
        return Ok(new { Message = "Cancellation requested" });
    }
}
