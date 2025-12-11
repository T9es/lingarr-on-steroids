using System.Security.Cryptography;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class MediaSubtitleProcessor : IMediaSubtitleProcessor
{
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ILogger<IMediaSubtitleProcessor> _logger;
    private readonly ISubtitleService _subtitleService;
    private readonly ISettingService _settingService;
    private readonly ISubtitleExtractionService _extractionService;
    private readonly LingarrDbContext _dbContext;
    private string _hash = string.Empty;
    private IMedia _media = null!;
    private MediaType _mediaType;

    public MediaSubtitleProcessor(
        ITranslationRequestService translationRequestService,
        ILogger<IMediaSubtitleProcessor> logger,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService extractionService,
        LingarrDbContext dbContext)
    {
        _translationRequestService = translationRequestService;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _extractionService = extractionService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ProcessMedia(
        IMedia media, 
        MediaType mediaType)
    {
        if (media.Path == null)
        {
            return false;
        }
        var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
        var matchingSubtitles = allSubtitles
            .Where(s => s.FileName.StartsWith(media.FileName + ".") || s.FileName == media.FileName)
            .ToList();

        if (!matchingSubtitles.Any())
        {
            return false;
        }

        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions);

        _media = media;
        _mediaType = mediaType;
        _hash = CreateHash(matchingSubtitles, sourceLanguages, targetLanguages, ignoreCaptions ?? "");
        if (!string.IsNullOrEmpty(media.MediaHash) && media.MediaHash == _hash)
        {
            return false;
        }
        
        _logger.LogInformation("Initiating subtitle processing.");
        return await ProcessSubtitles(matchingSubtitles, sourceLanguages, targetLanguages, ignoreCaptions ?? "");
    }

    /// <summary>
    /// Processes subtitle files for translation based on configured languages.
    /// </summary>
    /// <param name="subtitles">List of subtitle files to process.</param>
    /// <param name="sourceLanguages">The source languages.</param>
    /// <param name="targetLanguages">The target languages.</param>
    /// <param name="ignoreCaptions">The ignore captions setting.</param>
    /// <returns>True if new translation requests were created, false otherwise.</returns>
    private async Task<bool> ProcessSubtitles(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions)
    {
        var existingLanguages = ExtractLanguageCodes(subtitles);

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Source or target languages are empty. Source languages: {SourceCount}, Target languages: {TargetCount}",
                sourceLanguages.Count, targetLanguages.Count);
            await UpdateHash();
            return false;
        }

        var sourceLanguage = existingLanguages.FirstOrDefault(lang => sourceLanguages.Contains(lang));
        if (sourceLanguage != null && targetLanguages.Any())
        {
            // If ignoreCaptions is disabled, prefer non-caption source subtitles, but fallback to caption subtitles if no alternative exists
            var sourceSubtitle = ignoreCaptions == "true"
                ? subtitles.FirstOrDefault(s => s.Language == sourceLanguage && string.IsNullOrEmpty(s.Caption)) 
                    ?? subtitles.FirstOrDefault(s => s.Language == sourceLanguage)
                : subtitles.FirstOrDefault(s => s.Language == sourceLanguage);
                
            if (sourceSubtitle != null)
            {
                // Get languages that don't yet exist to validate whether captions in those languages are available
                var languagesToTranslate = targetLanguages.Except(existingLanguages);
                if (ignoreCaptions == "true")
                {
                    var targetLanguagesWithCaptions = subtitles
                        .Where(s => targetLanguages.Contains(s.Language) && !string.IsNullOrEmpty(s.Caption))
                        .Select(s => s.Language)
                        .Distinct()
                        .ToList();

                    if (targetLanguagesWithCaptions.Any())
                    {
                        _logger.LogInformation(
                            "Translation skipped because captions exist for target languages: |Green|{CaptionLanguages}|/Green| and ignoreCaptions is disabled",
                            string.Join(", ", targetLanguagesWithCaptions));
                        await UpdateHash();
                        return false;
                    }
                }

                foreach (var targetLanguage in languagesToTranslate)
                {
                    await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                    {
                        MediaId = _media.Id,
                        MediaType = _mediaType,
                        SubtitlePath = sourceSubtitle.Path,
                        TargetLanguage = targetLanguage,
                        SourceLanguage = sourceLanguage,
                        SubtitleFormat = sourceSubtitle.Format
                    });
                    _logger.LogInformation(
                        "Initiating translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{subtitleFile}|/Green|",
                        sourceLanguage,
                        targetLanguage,
                        sourceSubtitle.Path);
                }

                await UpdateHash();
                return true;
            }

            _logger.LogWarning("No source subtitle file found for language: |Green|{SourceLanguage}|/Green|",
                sourceLanguage);

            await UpdateHash();
            return false;
        }

        _logger.LogWarning(
            "No valid source language or target languages found for media |Green|{FileName}|/Green|. " +
            "Existing languages: |Red|{ExistingLanguages}|/Red|, " +
            "Source languages: |Red|{SourceLanguages}|/Red|, " +
            "Target languages: |Red|{TargetLanguages}|/Red|",
            string.Join(", ", _media?.FileName),
            string.Join(", ", existingLanguages),
            string.Join(", ", sourceLanguages),
            string.Join(", ", targetLanguages));

        await UpdateHash();
        return false;
    }

    /// <summary>
    /// Creates a hash of the current subtitle file state.
    /// </summary>
    /// <param name="subtitles">List of subtitle file paths to include in the hash.</param>
    /// <param name="sourceLanguages">The source languages.</param>
    /// <param name="targetLanguages">The target languages.</param>
    /// <param name="ignoreCaptions">The ignore captions setting.</param>
    /// <returns>A Base64 encoded string representing the hash of the current subtitle state.</returns>
    private string CreateHash(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions)
    {
        using var sha256 = SHA256.Create();
        var subtitlePaths = string.Join("|", subtitles.Select(subtitle => subtitle.Path)
            .ToList()
            .OrderBy(f => f));
        
        var sourceLangs = string.Join(",", sourceLanguages.OrderBy(l => l));
        var targetLangs = string.Join(",", targetLanguages.OrderBy(l => l));
        
        var hashInput = $"{subtitlePaths}|{sourceLangs}|{targetLangs}|{ignoreCaptions}";
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Extracts language codes from subtitle file names.
    /// </summary>
    /// <param name="subtitles">List of subtitle file paths to process.</param>
    /// <returns>A HashSet of valid language codes found in the file names.</returns>
    private HashSet<string> ExtractLanguageCodes(List<Subtitles> subtitles)
    {
        return subtitles
            .Select(s => s.Language.ToLowerInvariant())
            .ToHashSet();
    }

    /// <summary>
    /// Retrieves language settings from the application configuration.
    /// </summary>
    /// <typeparam name="T">The type of language setting to retrieve (Source or Target).</typeparam>
    /// <param name="settingName">The name of the setting to retrieve.</param>
    /// <returns>A HashSet of language codes from the configuration.</returns>
    private async Task<HashSet<string>> GetLanguagesSetting<T>(string settingName) where T : class, ILanguage
    {
        var languages = await _settingService.GetSettingAsJson<T>(settingName);
        return languages
            .Select(lang => lang.Code)
            .ToHashSet();
    }

    /// <summary>
    /// Updates the media hash in the database.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task UpdateHash()
    {
        _media.MediaHash = _hash;
        _dbContext.Update(_media);
        await _dbContext.SaveChangesAsync();
    }
    
    /// <inheritdoc />
    public async Task<int> ProcessMediaForceAsync(
        IMedia media, 
        MediaType mediaType,
        bool forceProcess = true)
    {
        if (media.Path == null)
        {
            return 0;
        }
        
        var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
        var matchingSubtitles = allSubtitles
            .Where(s => s.FileName.StartsWith(media.FileName + ".") || s.FileName == media.FileName)
            .ToList();

        _logger.LogDebug(
            "ProcessMediaForceAsync for {FileName}: Found {AllCount} subtitles in directory, {MatchCount} matching media file",
            media.FileName, allSubtitles.Count, matchingSubtitles.Count);
        
        if (!matchingSubtitles.Any())
        {
            _logger.LogInformation(
                "No external subtitles found for {FileName}. Checking for embedded subtitles...",
                media.FileName);
            
            // Try to queue translation jobs for embedded subtitle extraction
            return await TryQueueEmbeddedSubtitleTranslation(media, mediaType);
        }

        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions);

        _logger.LogDebug(
            "Language settings for {FileName}: Sources=[{Sources}], Targets=[{Targets}], IgnoreCaptions={IgnoreCaptions}",
            media.FileName, 
            string.Join(", ", sourceLanguages), 
            string.Join(", ", targetLanguages),
            ignoreCaptions);
        
        _logger.LogDebug(
            "Matching subtitles for {FileName}: [{Subtitles}]",
            media.FileName,
            string.Join(", ", matchingSubtitles.Select(s => $"{s.Language}:{s.FileName}")));

        _media = media;
        _mediaType = mediaType;
        _hash = CreateHash(matchingSubtitles, sourceLanguages, targetLanguages, ignoreCaptions ?? "");
        
        // If not forcing and hash matches, skip processing
        if (!forceProcess && !string.IsNullOrEmpty(media.MediaHash) && media.MediaHash == _hash)
        {
            _logger.LogDebug("Skipping {FileName}: hash matches and not forcing", media.FileName);
            return 0;
        }
        
        _logger.LogInformation("Initiating manual subtitle processing for {FileName} (forceProcess={Force}).", media.FileName, forceProcess);
        return await ProcessSubtitlesWithCount(matchingSubtitles, sourceLanguages, targetLanguages, ignoreCaptions ?? "", forceProcess);
    }
    
    /// <summary>
    /// Processes subtitle files for translation and returns the count of translations queued.
    /// </summary>
    /// <param name="forceTranslation">If true, translates to all target languages even if they already exist.</param>
    private async Task<int> ProcessSubtitlesWithCount(
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions,
        bool forceTranslation = false)
    {
        var existingLanguages = ExtractLanguageCodes(subtitles);
        var translationsQueued = 0;

        _logger.LogDebug(
            "ProcessSubtitlesWithCount: ExistingLanguages=[{Existing}], SourceLanguages=[{Sources}], TargetLanguages=[{Targets}], ForceTranslation={Force}",
            string.Join(", ", existingLanguages),
            string.Join(", ", sourceLanguages),
            string.Join(", ", targetLanguages),
            forceTranslation);

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Source or target languages are empty. Source languages: {SourceCount}, Target languages: {TargetCount}",
                sourceLanguages.Count, targetLanguages.Count);
            await UpdateHash();
            return 0;
        }

        var sourceLanguage = existingLanguages.FirstOrDefault(lang => sourceLanguages.Contains(lang));
        _logger.LogDebug("Source language match result: {SourceLanguage}", sourceLanguage ?? "NONE");
        
        if (sourceLanguage != null && targetLanguages.Any())

        {
            var sourceSubtitle = ignoreCaptions == "true"
                ? subtitles.FirstOrDefault(s => s.Language == sourceLanguage && string.IsNullOrEmpty(s.Caption)) 
                    ?? subtitles.FirstOrDefault(s => s.Language == sourceLanguage)
                : subtitles.FirstOrDefault(s => s.Language == sourceLanguage);
                
            if (sourceSubtitle != null)
            {
                // When forceTranslation is true, translate to all target languages even if they exist
                var languagesToTranslate = forceTranslation 
                    ? targetLanguages.AsEnumerable()
                    : targetLanguages.Except(existingLanguages);
                if (ignoreCaptions == "true")
                {
                    var targetLanguagesWithCaptions = subtitles
                        .Where(s => targetLanguages.Contains(s.Language) && !string.IsNullOrEmpty(s.Caption))
                        .Select(s => s.Language)
                        .Distinct()
                        .ToList();

                    if (targetLanguagesWithCaptions.Any())
                    {
                        _logger.LogInformation(
                            "Translation skipped because captions exist for target languages: |Green|{CaptionLanguages}|/Green|",
                            string.Join(", ", targetLanguagesWithCaptions));
                        await UpdateHash();
                        return 0;
                    }
                }

                foreach (var targetLanguage in languagesToTranslate)
                {
                    await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                    {
                        MediaId = _media.Id,
                        MediaType = _mediaType,
                        SubtitlePath = sourceSubtitle.Path,
                        TargetLanguage = targetLanguage,
                        SourceLanguage = sourceLanguage,
                        SubtitleFormat = sourceSubtitle.Format
                    });
                    translationsQueued++;
                    _logger.LogInformation(
                        "Initiating translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{subtitleFile}|/Green|",
                        sourceLanguage,
                        targetLanguage,
                        sourceSubtitle.Path);
                }

                await UpdateHash();
                return translationsQueued;
            }

            _logger.LogWarning("No source subtitle file found for language: |Green|{SourceLanguage}|/Green|",
                sourceLanguage);

            await UpdateHash();
            return 0;
        }

        _logger.LogWarning(
            "No valid source language or target languages found for media |Green|{FileName}|/Green|. " +
            "Existing languages: |Red|{ExistingLanguages}|/Red|, " +
            "Source languages: |Red|{SourceLanguages}|/Red|, " +
            "Target languages: |Red|{TargetLanguages}|/Red|",
            string.Join(", ", _media?.FileName),
            string.Join(", ", existingLanguages),
            string.Join(", ", sourceLanguages),
            string.Join(", ", targetLanguages));

        await UpdateHash();
        return 0;
    }
    
    /// <summary>
    /// Attempts to queue translation jobs for media with embedded subtitles but no external subtitles.
    /// </summary>
    /// <param name="media">The media item to process</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    /// <returns>The number of translation requests queued</returns>
    private async Task<int> TryQueueEmbeddedSubtitleTranslation(IMedia media, MediaType mediaType)
    {
        // Preserve the order of configured source languages so we can treat
        // them as a priority list (e.g. [en, ja] => prefer English when both
        // are good candidates, but fall back to Japanese when English only
        // has "Signs & Songs" style tracks).
        var sourceLanguageModels =
            await _settingService.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var configuredSourceLanguages = sourceLanguageModels
            .Select(lang => lang.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        var targetLanguageModels =
            await _settingService.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var targetLanguages = targetLanguageModels
            .Select(lang => lang.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet();
        
        if (configuredSourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Cannot queue embedded subtitle translation for {FileName}: source or target languages not configured",
                media.FileName);
            return 0;
        }
        
        // Sync embedded subtitles from the media file
        List<EmbeddedSubtitle>? embeddedSubtitles = null;
        
        if (mediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .FirstOrDefaultAsync(e => e.Id == media.Id);
                
            if (episode != null)
            {
                // Force sync to refresh embedded subtitles
                await _extractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                embeddedSubtitles = episode.EmbeddedSubtitles;
            }
        }
        else if (mediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == media.Id);
                
            if (movie != null)
            {
                // Force sync to refresh embedded subtitles
                await _extractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                embeddedSubtitles = movie.EmbeddedSubtitles;
            }
        }
        
        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
        {
            _logger.LogWarning(
                "No embedded subtitles found for {FileName}. Cannot translate.",
                media.FileName);
            return 0;
        }
        
        _logger.LogInformation(
            "Found {Count} embedded subtitles for {FileName}: [{Subtitles}]",
            embeddedSubtitles.Count, media.FileName,
            string.Join(", ", embeddedSubtitles.Select(s => $"{s.Language ?? "unknown"}:{s.CodecName}")));

        // Work only with text-based streams; image-based subtitles require OCR
        var textBasedSubs = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
        if (textBasedSubs.Count == 0)
        {
            _logger.LogWarning(
                "No text-based embedded subtitles found for {FileName}. Only image-based subtitles available.",
                media.FileName);
            return 0;
        }

        // Score candidates across all configured source languages.
        // We only consider streams whose language matches one of the
        // configured languages (via tolerant matching), and apply a small
        // priority bonus based on the language order.
        var scoredCandidates = new List<(EmbeddedSubtitle Subtitle, int Score, string MatchedLanguage, int LanguageIndex)>();

        foreach (var subtitle in textBasedSubs)
        {
            if (string.IsNullOrWhiteSpace(subtitle.Language))
            {
                continue;
            }

            var bestIndex = -1;
            string? matchedLanguage = null;

            for (var i = 0; i < configuredSourceLanguages.Count; i++)
            {
                var configuredLanguage = configuredSourceLanguages[i];
                if (SubtitleLanguageHelper.LanguageMatches(subtitle.Language, configuredLanguage))
                {
                    bestIndex = i;
                    matchedLanguage = configuredLanguage;
                    break;
                }
            }

            if (bestIndex == -1 || matchedLanguage == null)
            {
                // This subtitle is in a language the user didn't configure;
                // we'll surface it in logging but won't auto-translate from it.
                continue;
            }

            var baseScore = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, matchedLanguage);
            // Earlier languages in the list get a small priority boost,
            // but content quality (full vs signs/karaoke) dominates.
            var priorityBonus = (configuredSourceLanguages.Count - bestIndex) * 5;
            var totalScore = baseScore + priorityBonus;

            scoredCandidates.Add((subtitle, totalScore, matchedLanguage, bestIndex));
        }

        if (!scoredCandidates.Any())
        {
            var availableLanguages = textBasedSubs
                .GroupBy(s => SubtitleLanguageHelper.NormalizeLanguageCode(s.Language))
                .Select(g => g.Key ?? "unknown")
                .Distinct()
                .ToList();

            _logger.LogWarning(
                "No embedded subtitle matches configured source languages [{Sources}] for {FileName}. " +
                "Available embedded subtitle languages: [{Available}]. " +
                "Update your source languages on the Services page if you want to translate from one of these.",
                string.Join(", ", configuredSourceLanguages),
                media.FileName,
                string.Join(", ", availableLanguages));

            return 0;
        }

        var bestCandidate = scoredCandidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Subtitle.StreamIndex)
            .First();

        var selectedSubtitle = bestCandidate.Subtitle;
        var selectedSourceLanguage = bestCandidate.MatchedLanguage;

        _logger.LogInformation(
            "Selected embedded subtitle for translation: StreamIndex={StreamIndex}, LanguageTag={LanguageTag}, ConfiguredLanguage={ConfiguredLanguage}, Title=\"{Title}\", Codec={Codec}",
            selectedSubtitle.StreamIndex,
            selectedSubtitle.Language ?? "unknown",
            selectedSourceLanguage,
            selectedSubtitle.Title ?? "<none>",
            selectedSubtitle.CodecName);

        // Create translation requests for each target language (with empty subtitle path - TranslationJob will extract)
        var translationsQueued = 0;
        foreach (var targetLanguage in targetLanguages)
        {
            await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
            {
                MediaId = media.Id,
                MediaType = mediaType,
                SubtitlePath = null, // Will trigger embedded extraction in TranslationJob
                TargetLanguage = targetLanguage,
                SourceLanguage = selectedSourceLanguage,
                SubtitleFormat = null
            });
            translationsQueued++;
            _logger.LogInformation(
                "Queued embedded subtitle translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{FileName}|/Green|",
                selectedSourceLanguage,
                targetLanguage,
                media.FileName);
        }
        
        return translationsQueued;
    }
}

