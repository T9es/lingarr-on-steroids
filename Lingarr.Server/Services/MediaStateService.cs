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

        // 2. Get configured languages
        var sourceLanguages = await GetConfiguredLanguages(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetConfiguredLanguages(SettingKeys.Translation.TargetLanguages);

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            return TranslationState.NotApplicable;
        }

        // 3. Check for active translation request
        if (await HasActiveTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.InProgress;
        }

        // 3b. Check for failed translation request
        if (await HasFailedTranslationRequestAsync(media.Id, mediaType))
        {
            return TranslationState.Failed;
        }

        // 4. Get external subtitles
        var externalSubtitles = new List<Subtitles>();
        if (!string.IsNullOrEmpty(media.Path))
        {
            try
            {
                var allSubs = await _subtitleService.GetAllSubtitles(media.Path);
                var mediaNameNoExt = Path.GetFileNameWithoutExtension(media.FileName);
                externalSubtitles = allSubs
                    .Where(s => !string.IsNullOrEmpty(media.FileName) && 
                               (s.FileName.StartsWith(media.FileName + ".") || 
                                s.FileName == media.FileName ||
                                (!string.IsNullOrEmpty(mediaNameNoExt) && s.FileName.StartsWith(mediaNameNoExt + "."))))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get external subtitles for {Title}", media.Title);
            }
        }

        // 5. Check for source subtitle
        var hasExternalSource = externalSubtitles
            .Any(s => sourceLanguages.Any(sl => SubtitleLanguageHelper.LanguageMatches(s.Language, sl)));
        var hasEmbeddedSource = embeddedSubtitles
            .Any(e => e.IsTextBased && 
                     !string.IsNullOrEmpty(e.Language) && 
                     sourceLanguages.Any(sl => SubtitleLanguageHelper.LanguageMatches(e.Language, sl)));

        if (!hasExternalSource && !hasEmbeddedSource)
        {
            return TranslationState.AwaitingSource;
        }

        // 6. Check which targets are satisfied
        var existingTargetLanguages = externalSubtitles
            .Select(s => s.Language.ToLowerInvariant())
            .ToHashSet();

        var missingTargets = targetLanguages
            .Where(t => !existingTargetLanguages.Contains(t))
            .ToList();

        if (missingTargets.Count == 0)
        {
            return TranslationState.Complete;
        }

        // Has source, missing targets, no active request = Pending
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

        // Query movies needing work
        var moviesQuery = _dbContext.Movies
            .Include(m => m.EmbeddedSubtitles)
            .Where(m => !m.ExcludeFromTranslation)
            .Where(m => m.TranslationState == TranslationState.Pending 
                     || m.TranslationState == TranslationState.Stale
                     || m.TranslationState == TranslationState.Unknown
                     || (m.TranslationState == TranslationState.AwaitingSource && m.IndexedAt == null));

        if (priorityFirst)
        {
            moviesQuery = moviesQuery
                .OrderByDescending(m => m.IsPriority)
                .ThenBy(m => m.PriorityDate)
                .ThenBy(m => m.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(m => m.DateAdded);
        }
        else
        {
            moviesQuery = moviesQuery
                .OrderBy(m => m.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(m => m.DateAdded);
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
                     || e.TranslationState == TranslationState.Unknown
                     || (e.TranslationState == TranslationState.AwaitingSource && e.IndexedAt == null));

        if (priorityFirst)
        {
            episodesQuery = episodesQuery
                .OrderByDescending(e => e.Season.Show.IsPriority)
                .ThenBy(e => e.Season.Show.PriorityDate)
                .ThenBy(e => e.LastSubtitleCheckAt) // Oldest check first (nulls first usually)
                .ThenBy(e => e.DateAdded);
        }
        else
        {
            episodesQuery = episodesQuery
                .OrderBy(e => e.LastSubtitleCheckAt) // Oldest check first
                .ThenBy(e => e.DateAdded);
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

    /// <inheritdoc />
    public async Task UpdateLastSubtitleCheckAt(int mediaId, MediaType mediaType)
    {
        var now = DateTime.UtcNow;
        if (mediaType == MediaType.Movie)
        {
            await _dbContext.Movies
                .Where(m => m.Id == mediaId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastSubtitleCheckAt, now));
        }
        else
        {
            await _dbContext.Episodes
                .Where(e => e.Id == mediaId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastSubtitleCheckAt, now));
        }
    }
}
