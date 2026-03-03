using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
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
    private readonly ISubtitleExtractionService _extractionService;
    private readonly ILogger<TestTranslationController> _logger;
    
    public TestTranslationController(
        ITestTranslationService testTranslationService,
        LingarrDbContext dbContext,
        ISubtitleService subtitleService,
        ISubtitleExtractionService extractionService,
        ILogger<TestTranslationController> logger)
    {
        _testTranslationService = testTranslationService;
        _dbContext = dbContext;
        _subtitleService = subtitleService;
        _extractionService = extractionService;
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
            var movieQuery = _dbContext.Movies
                .Include(m => m.Images)
                .AsQueryable();

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

                var posterImage = movie.Images.FirstOrDefault(img => img.Type == "poster");
                var year = ExtractYearFromPath(movie.Path);

                results.Add(new TestTranslationSearchResult
                {
                    DisplayTitle = movie.Title,
                    MediaType = MediaType.Movie,
                    MediaId = movie.Id,
                    PosterPath = posterImage != null ? $"movie{posterImage.Path}" : null,
                    Year = year,
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
                .ThenInclude(s => s.Images)
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

                var posterImage = episode.Season.Show.Images.FirstOrDefault(img => img.Type == "poster");
                var year = ExtractYearFromPath(episode.Season.Show.Path);

                results.Add(new TestTranslationSearchResult
                {
                    DisplayTitle = displayTitle,
                    MediaType = MediaType.Episode,
                    MediaId = episode.Id,
                    PosterPath = posterImage != null ? $"show{posterImage.Path}" : null,
                    Year = year,
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

    /// <summary>
    /// Get subtitle preview for visual line picker.
    /// </summary>
    [HttpGet("subtitle-preview")]
    public async Task<ActionResult> GetSubtitlePreview(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return BadRequest(new { Message = "Path is required" });
        }

        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(path);
            
            var lines = subtitles.Select(s => new
            {
                Position = s.Position,
                StartTime = FormatTimestamp(s.StartTime),
                EndTime = FormatTimestamp(s.EndTime),
                Text = string.Join(" ", s.Lines)
            }).ToList();

            return Ok(new
            {
                TotalLines = subtitles.Count,
                Lines = lines
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read subtitle file: {Path}", path);
            return StatusCode(500, new { Message = $"Failed to read subtitle file: {ex.Message}" });
        }
    }

    private static string FormatTimestamp(int milliseconds)
    {
        var ts = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Search with hierarchical show→season→episode structure and fuzzy matching.
    /// Supports queries like "juju e4" to find specific episodes.
    /// </summary>
    [HttpGet("search-hierarchical")]
    public async Task<ActionResult<MediaSearchResult>> SearchHierarchical(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new MediaSearchResult());
        }

        var normalized = query.Trim().ToLowerInvariant();
        limit = Math.Clamp(limit, 1, 50);

        var result = new MediaSearchResult();

        try
        {
            var episodePattern = ParseEpisodeQuery(normalized);
            
            var movies = await SearchMovies(normalized, limit, cancellationToken);
            result.Movies = movies
                .Select(m => new MovieSearchResult
                {
                    Title = m.DisplayTitle,
                    MovieId = m.MediaId,
                    PosterPath = m.PosterPath,
                    Year = m.Year,
                    Subtitles = m.Subtitles.Select(s => new SubtitleInfo
                    {
                        Path = s.Path,
                        Language = s.Language,
                        FileName = s.FileName
                    }).ToList(),
                    EmbeddedSubtitles = m.EmbeddedSubtitles
                })
                .ToList();

            var shows = await SearchShows(normalized, episodePattern, limit, cancellationToken);
            result.Shows = shows;

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching media for test translation with query {Query}", query);
            return StatusCode(500, "Failed to search media for test translation.");
        }
    }

    private EpisodeQueryPattern? ParseEpisodeQuery(string query)
    {
        var patterns = new[]
        {
            Regex.Match(query, @"s(\d+)\s*e(\d+)"),
            Regex.Match(query, @"s(\d+)e(\d+)"),
            Regex.Match(query, @"(\d+)x(\d+)"),
            Regex.Match(query, @"\be(\d+)\b"),
            Regex.Match(query, @"ep(\d+)\b")
        };

        foreach (var match in patterns)
        {
            if (match.Success)
            {
                var season = match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var s) ? s : (int?)null;
                var episode = int.TryParse(match.Groups[^1].Value, out var e) ? e : (int?)null;
                
                var showTitle = Regex.Replace(query, @"(s\d+\s*e\d+|s\d+e\d+|\d+x\d+|\bep\d+|\be\d+)\b", "").Trim();
                
                return new EpisodeQueryPattern
                {
                    ShowTitle = showTitle,
                    SeasonNumber = season,
                    EpisodeNumber = episode
                };
            }
        }

        return null;
    }

    private async Task<List<TestTranslationSearchResult>> SearchMovies(
        string query, 
        int limit, 
        CancellationToken cancellationToken)
    {
        var movieQuery = _dbContext.Movies
            .Include(m => m.Images)
            .Include(m => m.EmbeddedSubtitles)
            .AsQueryable();

        // Priority: prefix match > substring match > filename match
        movieQuery = movieQuery.Where(m =>
            EF.Functions.ILike(m.Title, $"{query}%") ||
            EF.Functions.ILike(m.Title, $"%{query}%") ||
            (m.FileName != null && EF.Functions.ILike(m.FileName, $"%{query}%")));

        var movies = await movieQuery
            .OrderByDescending(m => EF.Functions.ILike(m.Title, $"{query}%") ? 1 : 0)
            .ThenByDescending(m => m.DateAdded)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var results = new ConcurrentBag<TestTranslationSearchResult>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

        await Parallel.ForEachAsync(movies, cancellationToken, async (movie, ct) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (string.IsNullOrEmpty(movie.Path))
                    return;

                var subtitles = await _subtitleService.GetAllSubtitles(movie.Path);
                if (subtitles.Count == 0)
                    return;

                // JIT sync embedded subtitles if not indexed (similar to SubtitleExtractionController)
                if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
                {
                    await _extractionService.SyncEmbeddedSubtitles(movie);
                    await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                }

                var posterImage = movie.Images.FirstOrDefault(img => img.Type == "poster");
                var year = ExtractYearFromPath(movie.Path);

                results.Add(new TestTranslationSearchResult
                {
                    DisplayTitle = movie.Title,
                    MediaType = MediaType.Movie,
                    MediaId = movie.Id,
                    PosterPath = posterImage != null ? $"movie{posterImage.Path}" : null,
                    Year = year,
                    Subtitles = subtitles,
                    EmbeddedSubtitles = movie.EmbeddedSubtitles
                        .Where(e => e.IsTextBased)
                        .Select(e => new EmbeddedSubtitleInfo
                        {
                            StreamIndex = e.StreamIndex,
                            Language = e.Language,
                            Title = e.Title,
                            CodecName = e.CodecName,
                            IsTextBased = e.IsTextBased,
                            IsDefault = e.IsDefault,
                            IsForced = e.IsForced
                        })
                        .ToList()
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        // Re-sort by prefix match priority after parallel execution
        return results
            .OrderByDescending(r => r.DisplayTitle.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(r => r.DisplayTitle)
            .ToList();
    }

    private async Task<List<ShowSearchResult>> SearchShows(
        string query,
        EpisodeQueryPattern? episodePattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var showTitle = episodePattern?.ShowTitle ?? query;
        
        var showQuery = _dbContext.Shows
            .Include(s => s.Images)
            .Include(s => s.Seasons)
            .ThenInclude(se => se.Episodes)
            .ThenInclude(e => e.EmbeddedSubtitles)
            .AsQueryable();

        // Priority: prefix match > substring match
        showQuery = showQuery.Where(s => 
            EF.Functions.ILike(s.Title, $"{showTitle}%") ||
            EF.Functions.ILike(s.Title, $"%{showTitle}%"));

        var shows = await showQuery
            .OrderByDescending(s => EF.Functions.ILike(s.Title, $"{showTitle}%") ? 1 : 0)
            .ThenByDescending(s => s.DateAdded)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var results = new List<ShowSearchResult>();

        foreach (var show in shows)
        {
            if (string.IsNullOrEmpty(show.Path))
                continue;

            var posterImage = show.Images.FirstOrDefault(img => img.Type == "poster");
            var year = ExtractYearFromPath(show.Path);

            var showResult = new ShowSearchResult
            {
                Title = show.Title,
                ShowId = show.Id,
                PosterPath = posterImage != null ? $"show{posterImage.Path}" : null,
                Year = year,
                Seasons = []
            };

            var seasonGroups = show.Seasons
                .Where(se => se.Episodes.Any())
                .GroupBy(se => se.SeasonNumber);

            foreach (var seasonGroup in seasonGroups)
            {
                var seasonPreview = new SeasonPreview
                {
                    SeasonNumber = seasonGroup.Key,
                    Episodes = []
                };

                foreach (var season in seasonGroup)
                {
                    var episodes = season.Episodes
                        .Where(e => e.Path != null || e.Season.Path != null)
                        .OrderBy(e => e.EpisodeNumber);

                    foreach (var episode in episodes)
                    {
                        if (episodePattern != null)
                        {
                            if (episodePattern.EpisodeNumber.HasValue && 
                                episode.EpisodeNumber != episodePattern.EpisodeNumber.Value)
                                continue;
                            
                            if (episodePattern.SeasonNumber.HasValue && 
                                season.SeasonNumber != episodePattern.SeasonNumber.Value)
                                continue;
                        }

                        var basePath = episode.Path ?? season.Path;
                        if (string.IsNullOrEmpty(basePath))
                            continue;

                        var subtitles = await _subtitleService.GetAllSubtitles(basePath);
                        if (subtitles.Count == 0)
                            continue;

                        // JIT sync embedded subtitles if not indexed (similar to SubtitleExtractionController)
                        if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
                        {
                            await _extractionService.SyncEmbeddedSubtitles(episode);
                            await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                        }

                        if (!string.IsNullOrEmpty(episode.FileName))
                        {
                            var fileName = episode.FileName.ToLowerInvariant();
                            subtitles = subtitles
                                .Where(s => s.FileName.ToLowerInvariant().Contains(fileName))
                                .ToList();

                            if (subtitles.Count == 0)
                                continue;
                        }

                        seasonPreview.Episodes.Add(new EpisodePreview
                        {
                            EpisodeId = episode.Id,
                            EpisodeNumber = episode.EpisodeNumber,
                            Title = episode.Title,
                            SeasonNumber = season.SeasonNumber,
                            Subtitles = subtitles.Select(s => new SubtitleInfo
                            {
                                Path = s.Path,
                                Language = s.Language,
                                FileName = s.FileName
                            }).ToList(),
                            EmbeddedSubtitles = episode.EmbeddedSubtitles
                                .Where(e => e.IsTextBased)
                                .Select(e => new EmbeddedSubtitleInfo
                                {
                                    StreamIndex = e.StreamIndex,
                                    Language = e.Language,
                                    Title = e.Title,
                                    CodecName = e.CodecName,
                                    IsTextBased = e.IsTextBased,
                                    IsDefault = e.IsDefault,
                                    IsForced = e.IsForced
                                })
                                .ToList()
                        });
                    }
                }

                if (seasonPreview.Episodes.Any())
                {
                    showResult.Seasons.Add(seasonPreview);
                }
            }

            if (showResult.Seasons.Any())
            {
                results.Add(showResult);
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts the year from a media path (e.g., "/movies/Movie Name (2024)/" -> 2024).
    /// </summary>
    private static int? ExtractYearFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(path, @"\((\d{4})\)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var year))
        {
            return year;
        }

        return null;
    }
}