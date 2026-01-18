using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

/// <summary>
/// Manages translation state for media items.
/// Provides efficient querying for items needing translation work.
/// </summary>
public class MediaStateService : IMediaStateService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly ILogger<MediaStateService> _logger;

    public MediaStateService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ILogger<MediaStateService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TranslationState> UpdateStateAsync(IMedia media, MediaType mediaType, bool saveChanges = true)
    {
        var currentVersion = await GetSettingsVersionAsync();
        
        // Get the actual entity for updating
        Movie? movie = null;
        Episode? episode = null;
        
        if (mediaType == MediaType.Movie)
        {
            movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == media.Id);
            if (movie == null) return TranslationState.Unknown;
        }
        else
        {
            episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .FirstOrDefaultAsync(e => e.Id == media.Id);
            if (episode == null) return TranslationState.Unknown;
        }
    
        var state = await ComputeStateAsync(
            movie as IMedia ?? episode!, 
            mediaType,
            movie?.EmbeddedSubtitles ?? episode!.EmbeddedSubtitles,
            movie?.ExcludeFromTranslation ?? episode!.ExcludeFromTranslation,
            episode?.Season?.ExcludeFromTranslation ?? false,
            episode?.Season?.Show?.ExcludeFromTranslation ?? false);
    
        // Update entity
        if (movie != null)
        {
            movie.TranslationState = state;
            movie.StateSettingsVersion = currentVersion;
        }
        else if (episode != null)
        {
            episode.TranslationState = state;
            episode.StateSettingsVersion = currentVersion;
        }
    
        if (saveChanges)
        {
            await _dbContext.SaveChangesAsync();
        }
        
        _logger.LogDebug(
            "Updated state for {Type} {Id} ({Title}): {State}",
            mediaType, media.Id, media.Title, state);

        return state;
    }

    /// <inheritdoc />
    public async Task UpdateStatesAsync(IEnumerable<IMedia> medias, MediaType mediaType, bool saveChanges = true)
    {
        var currentVersion = await GetSettingsVersionAsync();
        var sourceLanguages = await GetConfiguredLanguages(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetConfiguredLanguages(SettingKeys.Translation.TargetLanguages);

        // Ensure EmbeddedSubtitles are loaded for all media items
        var mediaIds = medias.Select(m => m.Id).ToList();
        if (mediaType == MediaType.Movie)
        {
            await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .Where(m => mediaIds.Contains(m.Id))
                .LoadAsync();
        }
        else
        {
            await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .Where(e => mediaIds.Contains(e.Id))
                .LoadAsync();
        }

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            foreach (var media in medias)
            {
                if (media is Movie m) { m.TranslationState = TranslationState.NotApplicable; m.StateSettingsVersion = currentVersion; }
                else if (media is Episode e) { e.TranslationState = TranslationState.NotApplicable; e.StateSettingsVersion = currentVersion; }
            }
            if (saveChanges) await _dbContext.SaveChangesAsync();
            return;
        }

        foreach (var media in medias)
        {
            List<EmbeddedSubtitle> embeddedSubtitles;
            bool mediaExcluded;
            bool seasonExcluded = false;
            bool showExcluded = false;

            if (media is Movie movie)
            {
                embeddedSubtitles = movie.EmbeddedSubtitles;
                mediaExcluded = movie.ExcludeFromTranslation;
            }
            else if (media is Episode episode)
            {
                embeddedSubtitles = episode.EmbeddedSubtitles;
                mediaExcluded = episode.ExcludeFromTranslation;
                seasonExcluded = episode.Season?.ExcludeFromTranslation ?? false;
                showExcluded = episode.Season?.Show?.ExcludeFromTranslation ?? false;
            }
            else
            {
                continue;
            }

            var state = await ComputeStateAsync(
                media,
                mediaType,
                embeddedSubtitles,
                mediaExcluded,
                seasonExcluded,
                showExcluded);

            if (media is Movie m) { m.TranslationState = state; m.StateSettingsVersion = currentVersion; }
            else if (media is Episode e) { e.TranslationState = state; e.StateSettingsVersion = currentVersion; }
        }

        if (saveChanges)
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<TranslationState> ComputeStateAsync(
        IMedia media,
        MediaType mediaType,
        List<EmbeddedSubtitle> embeddedSubtitles,
        bool mediaExcluded,
        bool seasonExcluded,
        bool showExcluded)
    {
        // 1. Check exclusions
        if (mediaExcluded || seasonExcluded || showExcluded)
        {
            return TranslationState.NotApplicable;
        }

        // 2. Check indexing status
        DateTime? indexedAt = null;
        if (media is Movie movie) indexedAt = movie.IndexedAt;
        else if (media is Episode episode) indexedAt = episode.IndexedAt;

        if (indexedAt == null)
        {
            return TranslationState.AwaitingSource;
        }

        // 3. Get configured languages
        var sourceLanguages = await GetConfiguredLanguages(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetConfiguredLanguages(SettingKeys.Translation.TargetLanguages);

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            return TranslationState.NotApplicable;
        }

        // 4. Check for existing subtitles in target language
        var hasAllTargets = targetLanguages.All(lang =>
            embeddedSubtitles.Any(s => s.Language?.ToLowerInvariant() == lang));
        
        if (hasAllTargets)
        {
            return TranslationState.Complete;
        }

        // 5. Check for source subtitles
        var hasSource = sourceLanguages.Any(lang =>
            embeddedSubtitles.Any(s => s.Language?.ToLowerInvariant() == lang));

        if (!hasSource)
        {
            return TranslationState.NoSuitableSubtitles;
        }

        // 6. Check for active/failed translation requests
        if (await HasActiveTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.InProgress;
        }

        if (await HasFailedTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.Failed;
        }

        // Has source, missing targets, no active/failed request = Pending
        return TranslationState.Pending;
    }


    /// <inheritdoc />
    public async Task MarkAllStaleAsync()
    {
        var movieCount = await _dbContext.Movies
            .Where(m => m.TranslationState != TranslationState.NotApplicable)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TranslationState, TranslationState.Stale));

        var episodeCount = await _dbContext.Episodes
            .Where(e => e.TranslationState != TranslationState.NotApplicable)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.TranslationState, TranslationState.Stale));

        _logger.LogInformation(
            "Marked {MovieCount} movies and {EpisodeCount} episodes as stale",
            movieCount, episodeCount);
    }

    /// <inheritdoc />
    public async Task<List<(IMedia Media, MediaType Type)>> GetMediaNeedingTranslationAsync(int limit, bool priorityFirst = true)
    {
        var result = new List<(IMedia Media, MediaType Type)>();
        var halfLimit = Math.Max(limit / 2, 1);

        _logger.LogInformation("Querying for media needing translation. Limit: {Limit}", limit);

        // Query movies needing work
        var moviesQuery = _dbContext.Movies
            .Include(m => m.EmbeddedSubtitles)
            .Where(m => !m.ExcludeFromTranslation)
            .Where(m => m.TranslationState == TranslationState.Pending
                     || m.TranslationState == TranslationState.Stale
                     || m.TranslationState == TranslationState.Failed
                     || m.TranslationState == TranslationState.Unknown
                     || m.TranslationState == TranslationState.AwaitingSource);

        var pendingCount = await _dbContext.Movies.CountAsync(m => m.TranslationState == TranslationState.Pending);
        var staleCount = await _dbContext.Movies.CountAsync(m => m.TranslationState == TranslationState.Stale);
        var unknownCount = await _dbContext.Movies.CountAsync(m => m.TranslationState == TranslationState.Unknown);
        var failedCount = await _dbContext.Movies.CountAsync(m => m.TranslationState == TranslationState.Failed);
        
        _logger.LogInformation("Movie states in DB: Pending={Pending}, Stale={Stale}, Unknown={Unknown}, Failed={Failed}", 
            pendingCount, staleCount, unknownCount, failedCount);

        if (priorityFirst)
        {
            moviesQuery = moviesQuery
                .OrderByDescending(m => m.IsPriority)
                .ThenBy(m => m.PriorityDate)
                .ThenBy(m => m.DateAdded);
        }
        else
        {
            moviesQuery = moviesQuery.OrderBy(m => m.DateAdded);
        }

        var movies = await moviesQuery.Take(halfLimit).ToListAsync();
        result.AddRange(movies.Select(m => ((IMedia)m, MediaType.Movie)));

        // Query episodes needing work  
        var episodesQuery = _dbContext.Episodes
            .Include(e => e.EmbeddedSubtitles)
            .Include(e => e.Season)
            .ThenInclude(s => s.Show)
            .Where(e => !e.ExcludeFromTranslation)
            .Where(e => !e.Season.ExcludeFromTranslation)
            .Where(e => !e.Season.Show.ExcludeFromTranslation)
            .Where(e => e.TranslationState == TranslationState.Pending
                     || e.TranslationState == TranslationState.Stale
                     || e.TranslationState == TranslationState.Failed
                     || e.TranslationState == TranslationState.Unknown
                     || e.TranslationState == TranslationState.AwaitingSource);

        if (priorityFirst)
        {
            episodesQuery = episodesQuery
                .OrderByDescending(e => e.Season.Show.IsPriority)
                .ThenBy(e => e.Season.Show.PriorityDate)
                .ThenBy(e => e.DateAdded);
        }
        else
        {
            episodesQuery = episodesQuery.OrderBy(e => e.DateAdded);
        }

        var episodes = await episodesQuery.Take(limit - movies.Count).ToListAsync();
        result.AddRange(episodes.Select(e => ((IMedia)e, MediaType.Episode)));

        return result;
    }

    /// <inheritdoc />
    public async Task<int> GetSettingsVersionAsync()
    {
        var versionStr = await _settingService.GetSetting(SettingKeys.Translation.LanguageSettingsVersion);
        return int.TryParse(versionStr, out var version) ? version : 1;
    }

    /// <inheritdoc />
    public async Task IncrementSettingsVersionAsync()
    {
        var current = await GetSettingsVersionAsync();
        var newVersion = current + 1;
        await _settingService.SetSetting(SettingKeys.Translation.LanguageSettingsVersion, newVersion.ToString());
        _logger.LogInformation("Incremented language settings version to {Version}", newVersion);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveTranslationRequestAsync(int mediaId, MediaType mediaType)
    {
        return await _dbContext.TranslationRequests.AnyAsync(tr =>
            tr.MediaId == mediaId &&
            tr.MediaType == mediaType &&
            (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress));
    }

    /// <inheritdoc />
    public async Task<bool> HasFailedTranslationRequestAsync(int mediaId, MediaType mediaType)
    {
        return await _dbContext.TranslationRequests.AnyAsync(tr =>
            tr.MediaId == mediaId &&
            tr.MediaType == mediaType &&
            tr.Status == TranslationStatus.Failed);
    }

    private async Task<HashSet<string>> GetConfiguredLanguages(string settingKey)
    {
        try
        {
            var languages = await _settingService.GetSettingAsJson<SourceLanguage>(settingKey);
            return languages.Select(l => l.Code.ToLowerInvariant()).ToHashSet();
        }
        catch
        {
            return new HashSet<string>();
        }
    }
}
