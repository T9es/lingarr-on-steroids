using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class AutomationService : IAutomationService
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly ICustomMediaSubtitleProcessor _customMediaSubtitleProcessor;
    private readonly ISettingService _settingService;
    private readonly IMediaStateService _mediaStateService;
    private readonly ICustomMediaStateService _customMediaStateService;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(
        LingarrDbContext dbContext,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        ICustomMediaSubtitleProcessor customMediaSubtitleProcessor,
        ISettingService settingService,
        IMediaStateService mediaStateService,
        ICustomMediaStateService customMediaStateService,
        ILogger<AutomationService> logger)
    {
        _dbContext = dbContext;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _customMediaSubtitleProcessor = customMediaSubtitleProcessor;
        _settingService = settingService;
        _mediaStateService = mediaStateService;
        _customMediaStateService = customMediaStateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ProcessSingleMediaForAutomationAsync(
        int mediaId,
        MediaType mediaType,
        string triggerSource)
    {
        var media = await LoadMediaAsync(mediaId, mediaType);
        if (media == null)
        {
            _logger.LogWarning(
                "Automation trigger '{TriggerSource}' skipped: media {MediaType} {MediaId} not found",
                triggerSource,
                mediaType,
                mediaId);
            return 0;
        }

        return await ProcessLoadedMediaForAutomationAsync(
            media,
            mediaType,
            triggerSource,
            updateRotationTimestamp: false,
            forceStateRefresh: true);
    }

    /// <inheritdoc />
    public async Task<int> ProcessLoadedMediaForAutomationAsync(
        IMedia media,
        MediaType mediaType,
        string triggerSource,
        bool updateRotationTimestamp = false,
        bool forceStateRefresh = false)
    {
        if (media is CustomMediaItem customMediaItem)
        {
            return await ProcessCustomMediaForAutomationAsync(
                customMediaItem,
                mediaType,
                triggerSource,
                updateRotationTimestamp,
                forceStateRefresh);
        }

        var settings = await _settingService.GetSettings([
            SettingKeys.Automation.AutomationEnabled,
            SettingKeys.Automation.MovieAgeThreshold,
            SettingKeys.Automation.ShowAgeThreshold
        ]);

        var automationEnabled =
            settings.GetValueOrDefault(SettingKeys.Automation.AutomationEnabled) == "true";

        var movieAgeThreshold = TimeSpan.FromHours(
            int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.MovieAgeThreshold), out var mh)
                ? mh
                : 0);
        var showAgeThreshold = TimeSpan.FromHours(
            int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.ShowAgeThreshold), out var sh)
                ? sh
                : 0);

        var currentVersion = await _mediaStateService.GetSettingsVersionAsync();
        var currentState = GetTranslationState(media, mediaType);
        var itemVersion = GetStateSettingsVersion(media, mediaType);
        var indexedAt = GetIndexedAt(media, mediaType);
        var shouldRefreshState = forceStateRefresh
            || currentState == TranslationState.Stale
            || currentState == TranslationState.Unknown
            || itemVersion < currentVersion;

        if (shouldRefreshState)
        {
            var previousState = currentState;
            currentState = await _mediaStateService.UpdateStateAsync(media, mediaType);
            _logger.LogDebug(
                "Automation trigger '{TriggerSource}' refreshed state for {MediaType} {MediaId} ({Title}) from {PreviousState} to {CurrentState}",
                triggerSource,
                mediaType,
                media.Id,
                media.Title,
                previousState,
                currentState);
        }

        if (!automationEnabled)
        {
            _logger.LogInformation(
                "Automation trigger '{TriggerSource}' skipped {MediaType} {MediaId} because automation is disabled",
                triggerSource,
                mediaType,
                media.Id);
            return 0;
        }

        if (!CanAttemptAutomation(currentState, indexedAt))
        {
            _logger.LogDebug(
                "Automation trigger '{TriggerSource}' skipped {MediaType} {MediaId} ({Title}) because state is {State}",
                triggerSource,
                mediaType,
                media.Id,
                media.Title,
                currentState);
            return 0;
        }

        var ageThreshold = ResolveAgeThreshold(media, mediaType, movieAgeThreshold, showAgeThreshold);
        if (!MeetsAgeThreshold(media, ageThreshold))
        {
            _logger.LogInformation(
                "Automation trigger '{TriggerSource}' skipped {MediaType} {MediaId} ({Title}) because age threshold is not met",
                triggerSource,
                mediaType,
                media.Id,
                media.Title);
            return 0;
        }

        if (updateRotationTimestamp)
        {
            await _mediaStateService.UpdateLastSubtitleCheckAt(media.Id, mediaType);
        }

        var forceProcess = currentState == TranslationState.Pending || currentState == TranslationState.Stale;
        var queuedCount = await _mediaSubtitleProcessor.ProcessMediaForceAsync(
            media,
            mediaType,
            forceProcess: forceProcess,
            forceTranslation: false);

        if (queuedCount > 0)
        {
            await _mediaStateService.UpdateStateAsync(media, mediaType);
            _logger.LogInformation(
                "Automation trigger '{TriggerSource}' queued {Count} translation request(s) for {MediaType} {MediaId} ({Title})",
                triggerSource,
                queuedCount,
                mediaType,
                media.Id,
                media.Title);
            return queuedCount;
        }

        var reconciledState = await _mediaStateService.UpdateStateAsync(media, mediaType);
        _logger.LogInformation(
            "Automation trigger '{TriggerSource}' finished {MediaType} {MediaId} ({Title}) without queueing new translations. Reconciled state: {State}",
            triggerSource,
            mediaType,
            media.Id,
            media.Title,
            reconciledState);
        return 0;
    }

    private async Task<int> ProcessCustomMediaForAutomationAsync(
        CustomMediaItem customMediaItem,
        MediaType mediaType,
        string triggerSource,
        bool updateRotationTimestamp,
        bool forceStateRefresh)
    {
        var item = await _dbContext.CustomMediaItems
            .Include(customItem => customItem.CustomSource)
            .FirstOrDefaultAsync(customItem => customItem.Id == customMediaItem.Id);

        if (item == null)
        {
            return 0;
        }

        if (!item.CustomSource.Enabled || !item.CustomSource.IncludeInAutomation)
        {
            return 0;
        }

        var settings = await _settingService.GetSettings([
            SettingKeys.Automation.AutomationEnabled,
            SettingKeys.Automation.MovieAgeThreshold,
            SettingKeys.Automation.ShowAgeThreshold
        ]);

        var automationEnabled =
            settings.GetValueOrDefault(SettingKeys.Automation.AutomationEnabled) == "true";

        var movieAgeThreshold = TimeSpan.FromHours(
            int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.MovieAgeThreshold), out var mh)
                ? mh
                : 0);
        var showAgeThreshold = TimeSpan.FromHours(
            int.TryParse(settings.GetValueOrDefault(SettingKeys.Automation.ShowAgeThreshold), out var sh)
                ? sh
                : 0);

        var currentVersion = await _customMediaStateService.GetSettingsVersionAsync();
        var currentState = item.TranslationState;
        var shouldRefreshState = forceStateRefresh
            || currentState == TranslationState.Stale
            || currentState == TranslationState.Unknown
            || currentState == TranslationState.Complete
            || item.StateSettingsVersion < currentVersion;

        if (shouldRefreshState)
        {
            currentState = await _customMediaStateService.UpdateStateAsync(item);
        }

        if (updateRotationTimestamp && currentState == TranslationState.Complete)
        {
            await _customMediaStateService.UpdateLastSubtitleCheckAt(item.Id);
        }

        if (!automationEnabled)
        {
            return 0;
        }

        if (!CanAttemptAutomation(currentState, item.IndexedAt))
        {
            return 0;
        }

        var ageThreshold = mediaType == MediaType.Movie ? movieAgeThreshold : showAgeThreshold;
        if (!MeetsAgeThreshold(item, ageThreshold))
        {
            return 0;
        }

        if (updateRotationTimestamp)
        {
            await _customMediaStateService.UpdateLastSubtitleCheckAt(item.Id);
        }

        var forceProcess = currentState == TranslationState.Pending || currentState == TranslationState.Stale;
        var queuedCount = await _customMediaSubtitleProcessor.ProcessCustomItemForceAsync(
            item,
            forceProcess: forceProcess,
            forceTranslation: false);

        if (queuedCount > 0)
        {
            await _customMediaStateService.UpdateStateAsync(item);
            return queuedCount;
        }

        await _customMediaStateService.UpdateStateAsync(item);
        return 0;
    }

    private async Task<IMedia?> LoadMediaAsync(int mediaId, MediaType mediaType)
    {
        if (mediaType == MediaType.Movie)
        {
            return await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == mediaId);
        }

        if (mediaType == MediaType.Episode)
        {
            return await _dbContext.Episodes
                .Include(e => e.Season)
                .ThenInclude(s => s.Show)
                .FirstOrDefaultAsync(e => e.Id == mediaId);
        }

        return null;
    }

    private static TranslationState GetTranslationState(IMedia media, MediaType mediaType)
    {
        return mediaType == MediaType.Movie
            ? ((Movie)media).TranslationState
            : ((Episode)media).TranslationState;
    }

    private static int GetStateSettingsVersion(IMedia media, MediaType mediaType)
    {
        return mediaType == MediaType.Movie
            ? ((Movie)media).StateSettingsVersion
            : ((Episode)media).StateSettingsVersion;
    }

    private static DateTime? GetIndexedAt(IMedia media, MediaType mediaType)
    {
        return mediaType == MediaType.Movie
            ? ((Movie)media).IndexedAt
            : ((Episode)media).IndexedAt;
    }

    private static bool CanAttemptAutomation(TranslationState state, DateTime? indexedAt)
    {
        return state == TranslationState.Pending
            || state == TranslationState.Stale
            || (state == TranslationState.AwaitingSource && indexedAt == null);
    }

    private static TimeSpan ResolveAgeThreshold(
        IMedia media,
        MediaType mediaType,
        TimeSpan movieAgeThreshold,
        TimeSpan showAgeThreshold)
    {
        if (mediaType == MediaType.Movie && media is Movie movie)
        {
            return movie.TranslationAgeThreshold.HasValue
                ? TimeSpan.FromHours(movie.TranslationAgeThreshold.Value)
                : movieAgeThreshold;
        }

        if (mediaType == MediaType.Episode && media is Episode episode)
        {
            var showThreshold = episode.Season?.Show?.TranslationAgeThreshold;
            return showThreshold.HasValue
                ? TimeSpan.FromHours(showThreshold.Value)
                : showAgeThreshold;
        }

        return TimeSpan.Zero;
    }

    private static bool MeetsAgeThreshold(IMedia media, TimeSpan threshold)
    {
        if (threshold == TimeSpan.Zero || media.DateAdded == null)
        {
            return true;
        }

        var age = DateTime.UtcNow - media.DateAdded.Value.ToUniversalTime();
        return age >= threshold;
    }
}
