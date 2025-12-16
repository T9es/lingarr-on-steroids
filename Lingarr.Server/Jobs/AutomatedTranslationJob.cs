using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class AutomatedTranslationJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<AutomatedTranslationJob> _logger;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ISettingService _settingService;
    private readonly IScheduleService _scheduleService;
    private readonly IMemoryCache _memoryCache;
    private int _maxTranslationsPerRun = 10;
    private TimeSpan _defaultMovieAgeThreshold;
    private TimeSpan _defaultShowAgeThreshold;

    private const string MovieProcessingIndexKey = "Automation:MovieProcessingIndex";
    private const string ShowProcessingIndexKey = "Automation:ShowProcessingIndex";

    public AutomatedTranslationJob(
        LingarrDbContext dbContext,
        ILogger<AutomatedTranslationJob> logger,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        IScheduleService scheduleService,
        ISettingService settingService,
        IMemoryCache memoryCache)
    {
        _dbContext = dbContext;
        _logger = logger;
        _settingService = settingService;
        _scheduleService = scheduleService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _memoryCache = memoryCache;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

        var settings = await _settingService.GetSettings([
            SettingKeys.Automation.AutomationEnabled,
            SettingKeys.Automation.TranslationCycle,
            SettingKeys.Automation.MaxTranslationsPerRun,
            SettingKeys.Automation.MovieAgeThreshold,
            SettingKeys.Automation.ShowAgeThreshold
        ]);

        if (settings[SettingKeys.Automation.AutomationEnabled] == "false")
        {
            _logger.LogInformation("Automation not enabled, skipping translation automation.");
            return;
        }

        int.TryParse(settings[SettingKeys.Automation.MaxTranslationsPerRun], out int maxTranslations);
        int.TryParse(settings[SettingKeys.Automation.MovieAgeThreshold], out int movieAgeThreshold);
        int.TryParse(settings[SettingKeys.Automation.ShowAgeThreshold], out int showAgeThreshold);

        _maxTranslationsPerRun = maxTranslations;
        _defaultMovieAgeThreshold = TimeSpan.FromHours(movieAgeThreshold);
        _defaultShowAgeThreshold = TimeSpan.FromHours(showAgeThreshold);

        var translationCycle = settings[SettingKeys.Automation.TranslationCycle] == "true" ? "movies" : "shows";
        _logger.LogInformation($"Starting translation cycle for |Green|{translationCycle}|/Green|");

        var translationsPerformed = 0;
        switch (translationCycle)
        {
            case "movies":
                await _settingService.SetSetting(SettingKeys.Automation.TranslationCycle, "false");
                translationsPerformed += await ProcessMovies(_maxTranslationsPerRun);
                if (translationsPerformed < _maxTranslationsPerRun)
                {
                    await ProcessShows(_maxTranslationsPerRun - translationsPerformed);
                }

                break;
            case "shows":
                await _settingService.SetSetting(SettingKeys.Automation.TranslationCycle, "true");
                translationsPerformed += await ProcessShows(_maxTranslationsPerRun);
                if (translationsPerformed < _maxTranslationsPerRun)
                {
                    await ProcessMovies(_maxTranslationsPerRun - translationsPerformed);
                }

                break;
        }

        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    private bool ShouldProcessMedia(IMedia media, MediaType mediaType, TimeSpan? customAgeThreshold = null)
    {
        if (media.Path == null)
        {
            return false;
        }

        TimeSpan fileAge;
        if (media.DateAdded.HasValue)
        {
            fileAge = DateTime.UtcNow - media.DateAdded.Value.ToUniversalTime();
        }
        else
        {
            var fileInfo = new FileInfo(media.Path);
            fileAge = DateTime.UtcNow - fileInfo.LastWriteTime.ToUniversalTime();
        }

        var threshold = customAgeThreshold ??
                        (mediaType == MediaType.Movie ? _defaultMovieAgeThreshold : _defaultShowAgeThreshold);

        var fileAgeHours = fileAge.TotalHours;
        var thresholdHours = threshold.TotalHours;
        if (!(fileAgeHours < thresholdHours))
        {
            return true;
        }

        _logger.LogInformation(
            "Media {FileName} does not meet age threshold. Age: {Age} hours, Required: {Threshold} hours",
            media.FileName,
            fileAgeHours.ToString("F2"),
            thresholdHours.ToString("F2"));
        return false;
    }

    private async Task<int> ProcessMovies(int limit)
    {
        _logger.LogInformation("Movie Translation job initiated");

        var movies = await _dbContext.Movies
            .Where(movie => !movie.ExcludeFromTranslation)
            .OrderByDescending(movie => movie.IsPriority)
            .ThenBy(movie => movie.PriorityDate)
            .ThenBy(movie => movie.Id)
            .ToListAsync();

        if (!movies.Any())
        {
            _logger.LogInformation("No translatable movies found.");
            return 0;
        }

        var priorityMovies = movies.Where(movie => movie.IsPriority).ToList();
        var normalMovies = movies.Where(movie => !movie.IsPriority).ToList();

        var translationsInitiated = 0;

        if (priorityMovies.Count > 0)
        {
            _logger.LogInformation("Processing {PriorityCount} priority movie(s) first.", priorityMovies.Count);
            foreach (var movie in priorityMovies)
            {
                if (translationsInitiated >= limit)
                {
                    _logger.LogInformation("Max translations per run reached while processing priority movies.");
                    break;
                }

                try
                {
                    TimeSpan? threshold = movie.TranslationAgeThreshold.HasValue
                        ? TimeSpan.FromHours(movie.TranslationAgeThreshold.Value)
                        : null;

                    if (!ShouldProcessMedia(movie, MediaType.Movie, threshold))
                    {
                        continue;
                    }

                    var translationsQueued =
                        await _mediaSubtitleProcessor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: false, forceTranslation: false);
                    if (translationsQueued > 0)
                    {
                        translationsInitiated++;
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    _logger.LogWarning("Directory not found at path: |Red|{Path}|/Red|, skipping subtitle", movie.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing subtitles for movie at path: |Red|{Path}|/Red|, skipping subtitle",
                        movie.Path);
                }
            }
        }

        if (translationsInitiated >= limit || normalMovies.Count == 0)
        {
            return translationsInitiated;
        }

        // Cycle through non-priority movies to avoid starvation,
        // but always process priority items first.
        var currentIndex = GetProcessingIndex(MovieProcessingIndexKey);
        if (currentIndex >= normalMovies.Count)
        {
            currentIndex = 0;
            _logger.LogInformation(
                "Movie processing cycle completed for non-priority movies. Starting new cycle from the beginning.");
        }

        var remainingLimit = limit - translationsInitiated;
        _logger.LogInformation(
            "Processing up to {MaxTranslations} non-priority movies starting at {StartIndex} out of {TotalCount}",
            remainingLimit,
            currentIndex,
            normalMovies.Count);

        var scannedMovies = 0;
        var index = currentIndex;

        while (translationsInitiated < limit && scannedMovies < normalMovies.Count)
        {
            var movie = normalMovies[index % normalMovies.Count];
            try
            {
                if (translationsInitiated >= limit)
                {
                    _logger.LogInformation("Max translations per run reached. Stopping translation process.");
                    break;
                }

                TimeSpan? threshold = movie.TranslationAgeThreshold.HasValue
                    ? TimeSpan.FromHours(movie.TranslationAgeThreshold.Value)
                    : null;

                if (!ShouldProcessMedia(movie, MediaType.Movie, threshold))
                {
                    continue;
                }

                var translationsQueued =
                    await _mediaSubtitleProcessor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: false, forceTranslation: false);
                if (translationsQueued > 0)
                {
                    translationsInitiated++;
                }
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogWarning("Directory not found at path: |Red|{Path}|/Red|, skipping subtitle", movie.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing subtitles for movie at path: |Red|{Path}|/Red|, skipping subtitle",
                    movie.Path);
            }
            finally
            {
                index++;
                scannedMovies++;
            }
        }

        var newIndex = index % normalMovies.Count;
        SetProcessingIndex(MovieProcessingIndexKey, newIndex);

        return translationsInitiated;
    }

    private async Task<int> ProcessShows(int limit)
    {
        _logger.LogInformation("Show Translation job initiated");

        var episodes = await _dbContext.Episodes
            .Include(e => e.Season)
            .ThenInclude(s => s.Show)
            .Where(episode =>
                !episode.ExcludeFromTranslation &&
                !episode.Season.ExcludeFromTranslation &&
                !episode.Season.Show.ExcludeFromTranslation)
            .OrderByDescending(e => e.Season.Show.IsPriority)
            .ThenBy(e => e.Season.Show.PriorityDate)
            .ThenBy(e => e.Id)
            .ToListAsync();

        if (!episodes.Any())
        {
            _logger.LogInformation("No translatable shows found.");
            return 0;
        }

        var priorityEpisodes = episodes.Where(e => e.Season.Show.IsPriority).ToList();
        var normalEpisodes = episodes.Where(e => !e.Season.Show.IsPriority).ToList();

        var translationsInitiated = 0;

        if (priorityEpisodes.Count > 0)
        {
            _logger.LogInformation("Processing {PriorityCount} priority episode(s) first.", priorityEpisodes.Count);
            foreach (var episode in priorityEpisodes)
            {
                if (translationsInitiated >= limit)
                {
                    _logger.LogInformation("Max translations per run reached while processing priority episodes.");
                    break;
                }

                try
                {
                    var show = episode.Season.Show;

                    TimeSpan? threshold = null;
                    if (show?.TranslationAgeThreshold.HasValue == true)
                    {
                        threshold = TimeSpan.FromHours(show.TranslationAgeThreshold.Value);
                    }

                    if (!ShouldProcessMedia(episode, MediaType.Episode, threshold))
                    {
                        continue;
                    }

                    var translationsQueued =
                        await _mediaSubtitleProcessor.ProcessMediaForceAsync(episode, MediaType.Episode,
                            forceProcess: false, forceTranslation: false);
                    if (translationsQueued > 0)
                    {
                        translationsInitiated++;
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    _logger.LogWarning("Directory not found for show at path: |Red|{Path}|/Red|, skipping episode",
                        episode.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error processing subtitles for episode at path: |Red|{Path}|/Red|, skipping episode",
                        episode.Path);
                }
            }
        }

        if (translationsInitiated >= limit || normalEpisodes.Count == 0)
        {
            return translationsInitiated;
        }

        // Cycle through non-priority episodes to avoid starvation,
        // but always process priority items first.
        var currentIndex = GetProcessingIndex(ShowProcessingIndexKey);
        if (currentIndex >= normalEpisodes.Count)
        {
            currentIndex = 0;
            _logger.LogInformation(
                "Show processing cycle completed for non-priority episodes. Starting new cycle from the beginning.");
        }

        var remainingLimit = limit - translationsInitiated;
        _logger.LogInformation(
            "Processing up to {MaxTranslations} non-priority episodes starting at {StartIndex} out of {TotalCount}",
            remainingLimit,
            currentIndex,
            normalEpisodes.Count);

        var scannedEpisodes = 0;
        var episodeIndex = currentIndex;

        while (translationsInitiated < limit && scannedEpisodes < normalEpisodes.Count)
        {
            var episode = normalEpisodes[episodeIndex % normalEpisodes.Count];

            try
            {
                var show = episode.Season.Show;

                TimeSpan? threshold = null;
                if (show?.TranslationAgeThreshold.HasValue == true)
                {
                    threshold = TimeSpan.FromHours(show.TranslationAgeThreshold.Value);
                }

                if (!ShouldProcessMedia(episode, MediaType.Episode, threshold))
                {
                    continue;
                }

                var translationsQueued =
                    await _mediaSubtitleProcessor.ProcessMediaForceAsync(episode, MediaType.Episode,
                        forceProcess: false, forceTranslation: false);
                if (translationsQueued > 0)
                {
                    translationsInitiated++;
                }
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogWarning("Directory not found for show at path: |Red|{Path}|/Red|, skipping episode",
                    episode.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing subtitles for episode at path: |Red|{Path}|/Red|, skipping episode",
                    episode.Path);
            }
            finally
            {
                episodeIndex++;
                scannedEpisodes++;
            }
        }

        var newIndex = episodeIndex % normalEpisodes.Count;
        SetProcessingIndex(ShowProcessingIndexKey, newIndex);

        return translationsInitiated;
    }

    private int GetProcessingIndex(string key)
    {
        if (!_memoryCache.TryGetValue(key, out int currentIndex))
        {
            currentIndex = 0;
        }
        return currentIndex;
    }
    
    private void SetProcessingIndex(string key, int value)
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.NeverRemove
        };
        
        _memoryCache.Set(key, value, cacheOptions);
    }
}
