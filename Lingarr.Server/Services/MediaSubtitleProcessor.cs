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
    private readonly ISubtitleIntegrityService _integrityService;
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".flv", ".webm" };
    private string _hash = string.Empty;
    private IMedia _media = null!;
    private MediaType _mediaType;

    public MediaSubtitleProcessor(
        ITranslationRequestService translationRequestService,
        ILogger<IMediaSubtitleProcessor> logger,
        ISettingService settingService,
        ISubtitleService subtitleService,
        ISubtitleExtractionService extractionService,
        ISubtitleIntegrityService integrityService,
        LingarrDbContext dbContext)
    {
        _translationRequestService = translationRequestService;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _extractionService = extractionService;
        _integrityService = integrityService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ProcessMedia(
        IMedia media, 
        MediaType mediaType)
    {
        var count = await ProcessInternalAsync(media, mediaType, forceProcess: false, forceTranslation: false, forcePriority: false);
        return count > 0;
    }

    /// <inheritdoc />
    public async Task<int> ProcessMediaForceAsync(
        IMedia media, 
        MediaType mediaType,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false)
    {
        return await ProcessInternalAsync(media, mediaType, forceProcess, forceTranslation, forcePriority);
    }

    private async Task<int> ProcessInternalAsync(
        IMedia media,
        MediaType mediaType,
        bool forceProcess,
        bool forceTranslation,
        bool forcePriority)
    {
        if (string.IsNullOrEmpty(media.Path) || string.IsNullOrEmpty(media.FileName))
        {
            return 0;
        }

        _media = media;
        _mediaType = mediaType;

        var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
        var matchingSubtitles = allSubtitles
            .Where(s => s.FileName.StartsWith(media.FileName + ".") || s.FileName == media.FileName)
            .ToList();

        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "false";

        if (sourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning("Source or target languages are empty for {Title}.", media.Title);
            return 0;
        }

        // 1. Check external subtitles first
        var existingLanguages = ExtractLanguageCodes(matchingSubtitles);
        var sourceLanguage = existingLanguages.FirstOrDefault(lang => sourceLanguages.Contains(lang));
        Subtitles? sourceSubtitle = null;

        if (sourceLanguage != null)
        {
            sourceSubtitle = ignoreCaptions == "true"
                 ? (matchingSubtitles.FirstOrDefault(s => s.Language == sourceLanguage && string.IsNullOrEmpty(s.Caption)) 
                     ?? matchingSubtitles.FirstOrDefault(s => s.Language == sourceLanguage))
                 : matchingSubtitles.FirstOrDefault(s => s.Language == sourceLanguage);
        }

        // 2. Compute Hash (Robust & Relative)
        _hash = CreateHash(media, matchingSubtitles, sourceLanguages, targetLanguages, ignoreCaptions);

        if (!forceProcess && !string.IsNullOrEmpty(media.MediaHash) && media.MediaHash == _hash)
        {
            _logger.LogDebug("Skipping {Title}: hash matches and not forcing", media.Title);
            return 0;
        }

        // 3. If no external source, try embedded
        if (sourceSubtitle == null)
        {
            _logger.LogInformation("No suitable external source found for {Title}, trying embedded...", media.Title);
            return await TryQueueEmbeddedSubtitleTranslation(media, mediaType, forceTranslation, forceProcess, forcePriority);
        }

        // 4. Process External Subtitles
        _logger.LogInformation("Processing external subtitles for {Title} (forceTranslation={Force}).", media.Title, forceTranslation);
        
        var languagesToTranslate = forceTranslation 
            ? targetLanguages.ToList()
            : targetLanguages.Except(existingLanguages).ToList();

        if (ignoreCaptions == "true" && !forceTranslation)
        {
            var targetLanguagesWithCaptions = matchingSubtitles
                .Where(s => targetLanguages.Contains(s.Language) && !string.IsNullOrEmpty(s.Caption))
                .Select(s => s.Language)
                .Distinct()
                .ToList();

            if (targetLanguagesWithCaptions.Any())
            {
                languagesToTranslate = languagesToTranslate.Except(targetLanguagesWithCaptions).ToList();
            }
        }

        var corruptLanguages = new List<string>();
        if (!forceTranslation)
        {
            foreach (var targetLang in targetLanguages.Intersect(existingLanguages))
            {
                var targetSubtitle = matchingSubtitles.FirstOrDefault(s => s.Language == targetLang);
                if (targetSubtitle != null)
                {
                    var isValid = await _integrityService.ValidateIntegrityAsync(sourceSubtitle.Path, targetSubtitle.Path);
                    if (!isValid)
                    {
                        _logger.LogWarning("Integrity check failed for {TargetLang} subtitle: {Path}", targetLang, targetSubtitle.Path);
                        corruptLanguages.Add(targetLang);
                    }
                }
            }
            languagesToTranslate = languagesToTranslate.Union(corruptLanguages).ToList();
        }

        var queuedCount = 0;
        foreach (var targetLanguage in languagesToTranslate)
        {
            if (await HasActiveRequestAsync(media.Id, mediaType, sourceLanguage!, targetLanguage)) continue;

            await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
            {
                MediaId = media.Id,
                MediaType = mediaType,
                SubtitlePath = sourceSubtitle.Path,
                TargetLanguage = targetLanguage,
                SourceLanguage = sourceLanguage!,
                SubtitleFormat = sourceSubtitle.Format
            }, forcePriority);
            queuedCount++;
        }

        if (corruptLanguages.Count == 0 && queuedCount > 0)
        {
            await UpdateHash();
        }
        else if (corruptLanguages.Count == 0 && languagesToTranslate.Count == 0)
        {
            // If nothing to translate and nothing corrupt, we can also update hash
            await UpdateHash();
            return 1; // Signal that we "processed" it (nothing needed)
        }

        return queuedCount;
    }

    private string CreateHash(
        IMedia media,
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions)
    {
        using var sha256 = SHA256.Create();
        
        // We no longer include media file size/mtime in the hash for external subtitles
        // to avoid re-translations when Tdarr/remuxing changes the container/video/audio
        // but leaves external .srt files untouched.

        var subtitleTokens = subtitles
            .OrderBy(s => s.Path)
            .Select(s => {
                var fileInfo = new FileInfo(s.Path);
                var size = fileInfo.Exists ? fileInfo.Length : 0;
                var mtime = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0;
                var relativePath = Path.GetFileName(s.Path); 
                return $"{relativePath}:{size}:{mtime}";
            });

        var hashInput = $"{string.Join("|", subtitleTokens)}|{string.Join(",", sourceLanguages.OrderBy(l => l))}|{string.Join(",", targetLanguages.OrderBy(l => l))}|{ignoreCaptions}|v6";
        return Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput)));
    }

    private string CreateEmbeddedHash(
        IMedia media,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IEnumerable<string> configuredSourceLanguages,
        IEnumerable<string> targetLanguages,
        string ignoreCaptions)
    {
        using var sha256 = SHA256.Create();

        // For embedded subtitles, we still need to know if the media file changed
        // because we might need to re-extract. However, Tdarr often changes the file
        // without changing the subtitle content.
        // We'll exclude mediaSize/mtime if we have embedded subtitles to track,
        // relying on the stream properties instead. If the streams change (e.g. re-ordered,
        // different codec), we'll still re-process.
        
        long mediaSize = 0;
        long mediaMtime = 0;
        
        // If no embedded subtitles found yet, we include media info to ensure
        // we re-probe when the file is replaced/updated.
        if (embeddedSubtitles.Count == 0)
        {
            try 
            {
                var dirInfo = new DirectoryInfo(media.Path!);
                if (dirInfo.Exists)
                {
                    var fileInfo = dirInfo.GetFiles(media.FileName + ".*")
                        .FirstOrDefault(f => VideoExtensions.Contains(f.Extension.ToLowerInvariant()));

                    if (fileInfo != null)
                    {
                        mediaSize = fileInfo.Length;
                        mediaMtime = fileInfo.LastWriteTimeUtc.Ticks;
                    }
                }
            } catch {}
        }

        var streamTokens = embeddedSubtitles
            .OrderBy(s => s.StreamIndex)
            .Select(s =>
                $"{s.StreamIndex}:{s.Language?.ToLowerInvariant()}:{s.CodecName}:{s.IsTextBased}:{s.IsDefault}:{s.IsForced}");

        var sources = string.Join(",", configuredSourceLanguages.OrderBy(l => l));
        var targets = string.Join(",", targetLanguages.OrderBy(l => l));

        var hashInput = $"{mediaSize}:{mediaMtime}|{string.Join("|", streamTokens)}|{sources}|{targets}|{ignoreCaptions}|v7";
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToBase64String(hashBytes);
    }

    private HashSet<string> ExtractLanguageCodes(List<Subtitles> subtitles)
    {
        return subtitles
            .Select(s => s.Language.ToLowerInvariant())
            .ToHashSet();
    }

    private async Task<HashSet<string>> GetLanguagesSetting<T>(string settingName) where T : class, ILanguage
    {
        var languages = await _settingService.GetSettingAsJson<T>(settingName);
        return languages
            .Select(lang => lang.Code.ToLowerInvariant())
            .ToHashSet();
    }

    private async Task UpdateHash()
    {
        _media.MediaHash = _hash;
        _dbContext.Update(_media);
        await _dbContext.SaveChangesAsync();
    }
    
    private async Task<int> TryQueueEmbeddedSubtitleTranslation(IMedia media, MediaType mediaType, bool forceTranslation, bool forceProcess, bool forcePriority = false)
    {
        if (string.IsNullOrEmpty(media.Path) || string.IsNullOrEmpty(media.FileName))
        {
            return 0;
        }

        var sourceLanguageModels =
            await _settingService.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var configuredSourceLanguages = sourceLanguageModels
            .Select(lang => lang.Code.ToLowerInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();

        var targetLanguageModels =
            await _settingService.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        var targetLanguages = targetLanguageModels
            .Select(lang => lang.Code.ToLowerInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet();

        var ignoreCaptions = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "false";
        
        if (configuredSourceLanguages.Count == 0 || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Cannot queue embedded subtitle translation for {FileName}: source or target languages not configured",
                media.FileName);
            return 0;
        }
        
        if (!forceProcess)
        {
            List<EmbeddedSubtitle>? cachedEmbedded = null;
            Movie? cachedMovie = null;
            Episode? cachedEpisode = null;
            
            if (mediaType == MediaType.Episode)
            {
                cachedEpisode = await _dbContext.Episodes
                    .Include(e => e.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(e => e.Id == media.Id);
                cachedEmbedded = cachedEpisode?.EmbeddedSubtitles;
            }
            else
            {
                cachedMovie = await _dbContext.Movies
                    .Include(m => m.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(m => m.Id == media.Id);
                cachedEmbedded = cachedMovie?.EmbeddedSubtitles;
            }
            
            var indexedAt = cachedMovie?.IndexedAt ?? cachedEpisode?.IndexedAt;
            if (cachedEmbedded != null && indexedAt != null)
            {
                var optimisticHash = CreateEmbeddedHash(media, cachedEmbedded, configuredSourceLanguages, targetLanguages, ignoreCaptions);
                var existingHash = cachedMovie?.MediaHash ?? cachedEpisode?.MediaHash;
                
                if (!string.IsNullOrEmpty(existingHash) && existingHash == optimisticHash)
                {
                    _logger.LogDebug(
                        "Optimistic skip for {FileName}: already indexed and hash matches",
                        media.FileName);
                    return 0;
                }
            }
        }
        
        List<EmbeddedSubtitle>? embeddedSubtitles = null;
        IMedia? trackedMedia = null;
        
        if (mediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(e => e.Id == media.Id);
                
            if (episode != null)
            {
                await _extractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                embeddedSubtitles = episode.EmbeddedSubtitles;
                trackedMedia = episode;
            }
        }
        else if (mediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(m => m.Id == media.Id);
                
            if (movie != null)
            {
                await _extractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                embeddedSubtitles = movie.EmbeddedSubtitles;
                trackedMedia = movie;
            }
        }
        
        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
        {
            _logger.LogWarning(
                "No embedded subtitles found for {FileName}. Cannot translate.",
                media.FileName);

            _media = trackedMedia ?? media;
            _mediaType = mediaType;
            _hash = CreateEmbeddedHash(_media, [], configuredSourceLanguages, targetLanguages, ignoreCaptions);
            await UpdateHash();

            return 0;
        }

        var mediaForHash = trackedMedia ?? media;
        var embeddedHash = CreateEmbeddedHash(mediaForHash, embeddedSubtitles, configuredSourceLanguages, targetLanguages, ignoreCaptions);

        if (!forceProcess && !string.IsNullOrEmpty(mediaForHash.MediaHash) && mediaForHash.MediaHash == embeddedHash)
        {
            _logger.LogDebug("Skipping embedded translation for {FileName}: hash matches and not forcing", media.FileName);
            return 0;
        }

        _media = mediaForHash;
        _mediaType = mediaType;
        _hash = embeddedHash;
        
        var textBasedSubs = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
        if (textBasedSubs.Count == 0)
        {
            _logger.LogWarning(
                "No text-based embedded subtitles found for {FileName}.",
                media.FileName);
            await UpdateHash();
            return 0;
        }

        var scoredCandidates = new List<(EmbeddedSubtitle Subtitle, int Score, string MatchedLanguage, int LanguageIndex)>();

        foreach (var subtitle in textBasedSubs)
        {
            if (string.IsNullOrWhiteSpace(subtitle.Language)) continue;

            var bestIndex = -1;
            string? matchedLanguage = null;

            for (var i = 0; i < configuredSourceLanguages.Count; i++)
            {
                if (SubtitleLanguageHelper.LanguageMatches(subtitle.Language, configuredSourceLanguages[i]))
                {
                    bestIndex = i;
                    matchedLanguage = configuredSourceLanguages[i];
                    break;
                }
            }

            if (bestIndex == -1 || matchedLanguage == null) continue;

            var baseScore = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, matchedLanguage);
            var priorityBonus = (configuredSourceLanguages.Count - bestIndex) * 5;
            var totalScore = baseScore + priorityBonus;

            scoredCandidates.Add((subtitle, totalScore, matchedLanguage, bestIndex));
        }

        if (!scoredCandidates.Any())
        {
            _logger.LogWarning("No embedded subtitle matches configured source languages for {FileName}.", media.FileName);
            await UpdateHash();
            return 0;
        }

        var bestCandidate = scoredCandidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Subtitle.StreamIndex)
            .First();

        var selectedSubtitle = bestCandidate.Subtitle;
        var selectedSourceLanguage = bestCandidate.MatchedLanguage;

        var allExternalSubtitles = await _subtitleService.GetAllSubtitles(media.Path!);
        var matchingExternalSubtitles = allExternalSubtitles
            .Where(s => s.FileName.StartsWith(media.FileName + ".") || s.FileName == media.FileName)
            .ToList();
        var existingExternalLanguages = matchingExternalSubtitles
            .Select(s => s.Language.ToLowerInvariant())
            .ToHashSet();

        var languagesToTranslate = forceTranslation
            ? targetLanguages.ToList()
            : targetLanguages.Except(existingExternalLanguages).ToList();

        string? tempSourcePath = null;
        var foundCorruption = false;
        try
        {
            var hasMatchingTarget = existingExternalLanguages.Any(lang => targetLanguages.Contains(lang));
                
            if (!forceTranslation && hasMatchingTarget)
            {
                var tempDir = Path.GetTempPath();
                tempSourcePath = await _extractionService.ExtractSubtitle(
                    Path.Combine(media.Path!, media.FileName!),
                    selectedSubtitle.StreamIndex,
                    tempDir,
                    "srt",
                    selectedSourceLanguage);

                if (tempSourcePath != null)
                {
                    var corruptLanguages = new List<string>();
                    foreach (var targetLang in targetLanguages.Intersect(existingExternalLanguages))
                    {
                        var targetSubtitle = matchingExternalSubtitles.FirstOrDefault(s => 
                            s.Language.Equals(targetLang, StringComparison.OrdinalIgnoreCase));
                        if (targetSubtitle != null)
                        {
                            var isValid = await _integrityService.ValidateIntegrityAsync(tempSourcePath, targetSubtitle.Path);
                            if (!isValid)
                            {
                                _logger.LogWarning("Integrity check failed for {TargetLang} subtitle: {Path} - scheduling re-translation", targetLang, targetSubtitle.Path);
                                corruptLanguages.Add(targetLang);
                            }
                        }
                    }

                    if (corruptLanguages.Count > 0) foundCorruption = true;
                    languagesToTranslate = languagesToTranslate.Union(corruptLanguages).ToList();
                }
            }

            var translationsQueued = 0;
            foreach (var targetLanguage in languagesToTranslate)
            {
                if (await HasActiveRequestAsync(media.Id, mediaType, selectedSourceLanguage, targetLanguage)) continue;

                await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                {
                    MediaId = media.Id,
                    MediaType = mediaType,
                    SubtitlePath = null,
                    TargetLanguage = targetLanguage,
                    SourceLanguage = selectedSourceLanguage,
                    SubtitleFormat = null
                }, forcePriority);
                translationsQueued++;
            }

            if (!foundCorruption) await UpdateHash();
            return translationsQueued;
        }
        finally
        {
            if (tempSourcePath != null && File.Exists(tempSourcePath))
            {
                try { File.Delete(tempSourcePath); } catch {}
            }
        }
    }

    private async Task<bool> HasActiveRequestAsync(int mediaId, MediaType mediaType, string sourceLanguage, string targetLanguage)
    {
        return await _dbContext.TranslationRequests.AnyAsync(tr =>
            tr.MediaId == mediaId &&
            tr.MediaType == mediaType &&
            tr.SourceLanguage == sourceLanguage &&
            tr.TargetLanguage == targetLanguage &&
            (tr.Status == TranslationStatus.Pending || tr.Status == TranslationStatus.InProgress));
    }
}
