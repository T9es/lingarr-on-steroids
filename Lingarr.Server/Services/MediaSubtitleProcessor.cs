using System.Security.Cryptography;
using Hangfire;
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
using Lingarr.Server.Models.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ISourceSubtitleSnapshotService _sourceSubtitleSnapshotService;
    private readonly ISubtitleSourceSelectionService _subtitleSourceSelectionService;
    private readonly ISubtitleOcrService? _subtitleOcrService;
    private readonly ITranslationDiagnosticsService? _diagnosticsService;
    private readonly IBackgroundJobClient? _backgroundJobClient;
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
        ISourceSubtitleSnapshotService sourceSubtitleSnapshotService,
        LingarrDbContext dbContext,
        ISubtitleSourceSelectionService? subtitleSourceSelectionService = null,
        ISubtitleOcrService? subtitleOcrService = null,
        ITranslationDiagnosticsService? diagnosticsService = null,
        IBackgroundJobClient? backgroundJobClient = null)
    {
        _translationRequestService = translationRequestService;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _extractionService = extractionService;
        _integrityService = integrityService;
        _sourceSubtitleSnapshotService = sourceSubtitleSnapshotService;
        _subtitleOcrService = subtitleOcrService;
        _diagnosticsService = diagnosticsService;
        _backgroundJobClient = backgroundJobClient;
        _subtitleSourceSelectionService = subtitleSourceSelectionService ??
            new SubtitleSourceSelectionService(
                subtitleService,
                NullLogger<SubtitleSourceSelectionService>.Instance);
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
        var knownGeneratedPaths = await GetKnownGeneratedSubtitlePathsAsync(media.Id, mediaType);
        var matchingSubtitles = MediaSubtitleMatcher.FilterMatchingSubtitles(
            media.FileName,
            allSubtitles,
            knownGeneratedPaths);

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
        var isAutoMode = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.SourceLanguageMode),
            "auto",
            StringComparison.OrdinalIgnoreCase);

        if ((!isAutoMode && sourceLanguages.Count == 0) || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Source or target languages are empty for subtitle processing. Auto mode: {IsAutoMode}. Source languages: {SourceCount}, Target languages: {TargetCount}",
                isAutoMode,
                sourceLanguages.Count,
                targetLanguages.Count);
            await UpdateHash();
            return false;
        }

        string? tempSourcePath = null;
        try
        {
            ResolvedExternalSourceSubtitle? resolvedExternalSource;
            if (isAutoMode)
            {
                resolvedExternalSource = await _sourceSubtitleSnapshotService.ResolveExternalSourceWithAutoAsync(
                    _media,
                    subtitles,
                    true,
                    targetLanguages.ToList());
            }
            else
            {
                resolvedExternalSource = await _sourceSubtitleSnapshotService.ResolveExternalSourceAsync(
                    _media,
                    subtitles);
            }
            var sourceLanguage = resolvedExternalSource?.SourceLanguage;
            var sourceSubtitle = resolvedExternalSource?.Subtitle;
            var sourceSnapshot = resolvedExternalSource?.Snapshot;

            if (sourceSubtitle != null)
            {
                if (ExternalSubtitleCandidateHelper.IsSparseSubtitleFile(sourceSubtitle))
                {
                    var entryCount = SubtitleExtractionService.CountSubtitleEntries(sourceSubtitle.Path);
                    _logger.LogWarning(
                        "External subtitle {Path} is sparse ({Count} entries, minimum: {Min}). Skipping it as a primary source and trying embedded fallback.",
                        sourceSubtitle.Path, 
                        entryCount, 
                        SubtitleExtractionService.MinimumDialogueEntries);
                    sourceSubtitle = null;
                    sourceSnapshot = null;
                }
                else if (await IsCorruptExternalSourceAsync(sourceSubtitle))
                {
                    _logger.LogWarning(
                        "External subtitle {Path} looks like OCR/random-text garbage. Skipping it as a primary source and trying embedded fallback.",
                        sourceSubtitle.Path);
                    sourceSubtitle = null;
                    sourceSnapshot = null;
                }
            }

            // Fallback: If no external source found (even if sourceLanguage was detected but file missing?)
            // Actually, existingLanguages comes from files. So if sourceLanguage != null, the file exists.
            // But if existingLanguages DOES NOT contain sourceLanguage, sourceLanguage is null.
            // So we check if validSource is missing.
            
            if (sourceSubtitle == null)
            {
                _logger.LogInformation("No external source subtitle found for {FileName}. Checking for embedded subtitles for validation...", _media.FileName);
                
                // Logic to extract embedded subtitle
                var sourceLanguageModels = await _settingService.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
                var configuredSourceLanguages = sourceLanguageModels.Select(lang => lang.Code).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

                var embeddedSubtitles = await ProbeEmbeddedSubtitlesForCurrentMedia();
                
                if (embeddedSubtitles != null && embeddedSubtitles.Any())
                {
                 var textBasedSubs = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
                     var bestMatch = await _subtitleSourceSelectionService.SelectPrimaryAsync(
                         textBasedSubs,
                         configuredSourceLanguages,
                         allowCaptionFallback: !string.Equals(ignoreCaptions, "true", StringComparison.OrdinalIgnoreCase),
                         targetLanguages: isAutoMode ? targetLanguages.ToList() : null);
                     
                     if (bestMatch.SelectedSubtitle != null)
                     {
                         var tempDir = Path.GetTempPath();
                         var tempFileName = $"lingarr_temp_source_{Guid.NewGuid()}.{bestMatch.MatchedLanguage}.srt";
                         
                         tempSourcePath = await _extractionService.ExtractSubtitle(
                             Path.Combine(_media.Path!, _media.FileName!),
                             bestMatch.SelectedSubtitle.StreamIndex,
                             tempDir,
                             "srt",
                             bestMatch.MatchedLanguage);
                             
                         if (tempSourcePath != null)
                         {
                             // Create a temporary Subtitles object
                             sourceSubtitle = new Subtitles
                             {
                                 Path = tempSourcePath,
                                 Language = bestMatch.MatchedLanguage,
                                 Format = "srt",
                                 FileName = Path.GetFileName(tempSourcePath)
                             };
                             sourceLanguage = bestMatch.MatchedLanguage;
                             sourceSnapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(
                                 bestMatch.SelectedSubtitle,
                                 bestMatch.MatchedLanguage);
                             _logger.LogInformation("Extracted temporary source subtitle for validation: {TempPath}", tempSourcePath);
                         }
                     }
                }
            }

            if (sourceSubtitle != null)
            {
                var requestedRequiredOutputFormats =
                    await GetRequestedRequiredOutputFormatsAsync(sourceSubtitle.Format);
                var requiredOutputFormats =
                    SubtitleOutputModeHelper.DeserializeFormats(requestedRequiredOutputFormats);
                var languagesToTranslate = GetLanguagesMissingRequiredOutputFormats(
                        subtitles,
                        targetLanguages,
                        requiredOutputFormats)
                    .ToList();
                
                // Check integrity of existing target subtitles and add corrupt ones for re-translation
                var corruptLanguages = new List<string>();
                foreach (var targetLang in targetLanguages.Intersect(existingLanguages))
                {
                    var targetSubtitle = SelectMainTargetSubtitle(subtitles, targetLang);
                    if (targetSubtitle != null)
                    {
                        var isValid = await _integrityService.ValidateIntegrityAsync(
                            sourceSubtitle.Path, 
                            targetSubtitle.Path);
                        if (!isValid)
                        {
                            _logger.LogWarning(
                                "Integrity check failed for {TargetLang} subtitle: {Path} - scheduling re-translation",
                                targetLang, targetSubtitle.Path);
                            corruptLanguages.Add(targetLang);
                        }
                    }
                }
                
                // Add corrupt languages to the translation queue
                languagesToTranslate = languagesToTranslate.Union(corruptLanguages).ToList();
                var foundCorruption = corruptLanguages.Count > 0;
                
                if (ignoreCaptions == "true")
                {
                    var targetLanguagesWithCaptions = subtitles
                        .Where(s =>
                            targetLanguages.Contains(s.Language) &&
                            !string.IsNullOrEmpty(s.Caption) &&
                            !ExternalSubtitleCandidateHelper.ShouldSkipAsMainTarget(s))
                        .Select(s => s.Language)
                        .Distinct()
                        .ToList();

                    if (targetLanguagesWithCaptions.Any())
                    {
                        // Remove languages that have captions from languagesToTranslate if ignoreCaptions is true
                        // Actually logic above just returns. But if we have corrupt languages, maybe we want to continue?
                        // Original logic returns if ANY valid caption exists? No, it returns if target exists w/ caption.
                        // Let's keep original logic strictness for now.
                        
                        // BUT: corruptLanguages might NEED re-translation. If a corrupt subtitle has a caption, do we skip it?
                        // If it's corrupt, it's corrupt.
                        
                        var skipped = targetLanguagesWithCaptions.Except(corruptLanguages).ToList();
                        if (skipped.Any())
                        {
                            _logger.LogInformation(
                                "Translation skipped because captions exist for target languages: |Green|{CaptionLanguages}|/Green| and ignoreCaptions is disabled",
                                string.Join(", ", skipped));
                            
                           // If all targets are skipped, return.
                           if (!languagesToTranslate.Except(skipped).Any())
                           {
                               if (!foundCorruption)
                               {
                                   await UpdateHash();
                               }
                               return false;
                           }
                        }
                    }
                }

                foreach (var targetLanguage in languagesToTranslate)
                {
                    if (sourceLanguage == null)
                    {
                        _logger.LogInformation(
                            "Skipping enqueue for {FileName} {Source}->{Target}: translation request already active.",
                            _media.FileName,
                            sourceLanguage,
                            targetLanguage);
                        continue;
                    }

                    if (await HasActiveRequestAsync(
                            _media.Id,
                            _mediaType,
                            sourceLanguage,
                            targetLanguage,
                            requestedRequiredOutputFormats))
                    {
                        _logger.LogInformation(
                            "Skipping enqueue for {FileName} {Source}->{Target}: translation request already active.",
                            _media.FileName,
                            sourceLanguage,
                            targetLanguage);
                        continue;
                    }

                    await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                    {
                        MediaId = _media.Id,
                        MediaType = _mediaType,
                        SubtitlePath = (tempSourcePath != null) ? null : sourceSubtitle.Path, // If temp, use NULL so Job extracts fresh? Or use temp? 
                        // IMPORTANT: If we use temp path, the Job might fail if temp is deleted.
                        // Ideally, we pass NULL so the Job does its own extraction. 
                        // We ONLY extracted temp for VALIDATION.
                        TargetLanguage = targetLanguage,
                        SourceLanguage = sourceLanguage,
                        SubtitleFormat = sourceSubtitle.Format,
                        SourceSubtitleType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(sourceSubtitle.Path),
                        SourceSnapshot = sourceSnapshot
                    });
                    _logger.LogInformation(
                        "Initiating translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{subtitleFile}|/Green|",
                        sourceLanguage,
                        targetLanguage,
                        sourceSubtitle.Path);
                }

                // Only update hash if no corruption was found - ensures re-validation if translation fails
                if (!foundCorruption)
                {
                    await UpdateHash();
                }
                else
                {
                    _logger.LogDebug("Skipping hash update for {FileName} due to corruption found - will re-validate next run", _media.FileName);
                }
                return true;
            }

            _logger.LogWarning("No source subtitle file found for language: |Green|{SourceLanguage}|/Green|",
                sourceLanguage);

            await UpdateHash();
            return false;
        }
        finally
        {
            if (tempSourcePath != null && File.Exists(tempSourcePath))
            {
                try 
                { 
                    File.Delete(tempSourcePath); 
                    _logger.LogDebug("Deleted temporary validation subtitle: {TempPath}", tempSourcePath);
                }
                catch (Exception ex) 
                {
                    _logger.LogWarning(ex, "Failed to delete temporary validation subtitle: {TempPath}", tempSourcePath);
                }
            }
        }
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
        
        var hashInput = $"{subtitlePaths}|{sourceLangs}|{targetLangs}|{ignoreCaptions}|v2";
	        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
	        return Convert.ToBase64String(hashBytes);
	    }

	    private string CreateEmbeddedHash(
	        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
	        IEnumerable<string> configuredSourceLanguages,
	        IEnumerable<string> targetLanguages)
	    {
	        using var sha256 = SHA256.Create();

	        var streamTokens = embeddedSubtitles
	            .OrderBy(s => s.StreamIndex)
	            .Select(s =>
                $"{s.StreamIndex}:{s.Language?.ToLowerInvariant()}:{s.CodecName}:{s.IsTextBased}:{s.IsDefault}:{s.IsForced}:{s.OcrStatus}:{s.OcrQualityScore}:{s.OcrCueCount}");

	        var sources = string.Join(",", configuredSourceLanguages.OrderBy(l => l));
	        var targets = string.Join(",", targetLanguages.OrderBy(l => l));

        var hashInput = $"{string.Join("|", streamTokens)}|{sources}|{targets}|v3";
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
            .Select(lang => lang.Code.ToLowerInvariant())
            .ToHashSet();
    }

    private static HashSet<string> FilterTargetLanguages(
        HashSet<string> configuredTargetLanguages,
        IReadOnlyCollection<string>? requestedTargetLanguages)
    {
        if (requestedTargetLanguages == null || requestedTargetLanguages.Count == 0)
        {
            return configuredTargetLanguages;
        }

        var requested = requestedTargetLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requested.Count == 0)
        {
            return [];
        }

        return configuredTargetLanguages
            .Where(language => requested.Contains(SubtitleLanguageHelper.NormalizeLanguageCode(language)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null)
    {
        return await ProcessMediaForceAsync(
            media,
            mediaType,
            forceProcess,
            forceTranslation,
            forcePriority,
            queueTranslations,
            maxTranslationsToQueue,
            new List<SubtitleIntegrityFinding>());
    }

    /// <inheritdoc />
    public async Task<int> ProcessMediaForceAsync(
        IMedia media,
        MediaType mediaType,
        bool forceProcess,
        bool forceTranslation,
        bool forcePriority,
        bool queueTranslations,
        int? maxTranslationsToQueue,
        ICollection<SubtitleIntegrityFinding> integrityFindings)
    {
        return await ProcessMediaForceCoreAsync(
            media,
            mediaType,
            forceProcess,
            forceTranslation,
            forcePriority,
            queueTranslations,
            maxTranslationsToQueue,
            integrityFindings,
            targetLanguageFilter: null);
    }

    /// <inheritdoc />
    public async Task<int> ProcessMediaForceTargetAsync(
        IMedia media,
        MediaType mediaType,
        string targetLanguage,
        bool forceProcess = true,
        bool forceTranslation = true,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null)
    {
        return await ProcessMediaForceCoreAsync(
            media,
            mediaType,
            forceProcess,
            forceTranslation,
            forcePriority,
            queueTranslations,
            maxTranslationsToQueue,
            new List<SubtitleIntegrityFinding>(),
            [targetLanguage]);
    }

    private async Task<int> ProcessMediaForceCoreAsync(
        IMedia media,
        MediaType mediaType,
        bool forceProcess,
        bool forceTranslation,
        bool forcePriority,
        bool queueTranslations,
        int? maxTranslationsToQueue,
        ICollection<SubtitleIntegrityFinding> integrityFindings,
        IReadOnlyCollection<string>? targetLanguageFilter)
    {
        if (media.Path == null)
        {
            return 0;
        }
        
        var allSubtitles = await _subtitleService.GetAllSubtitles(media.Path);
        var matchingSubtitles = MediaSubtitleMatcher.FilterMatchingSubtitles(
            media.FileName,
            allSubtitles,
            await GetKnownGeneratedSubtitlePathsAsync(media.Id, mediaType));

        _logger.LogDebug(
            "ProcessMediaForceAsync for {FileName}: Found {AllCount} subtitles in directory, {MatchCount} matching media file",
            media.FileName, allSubtitles.Count, matchingSubtitles.Count);
        
        if (!matchingSubtitles.Any())
        {
            _logger.LogInformation(
                "No external subtitles found for {FileName}. Checking for embedded subtitles...",
                media.FileName);
            
            // Try to queue translation jobs for embedded subtitle extraction
            return await TryQueueEmbeddedSubtitleTranslation(
                media,
                mediaType,
                forceTranslation,
                forceProcess,
                forcePriority,
                queueTranslations,
                maxTranslationsToQueue,
                integrityFindings);
        }

        var sourceLanguages = await GetLanguagesSetting<SourceLanguage>(SettingKeys.Translation.SourceLanguages);
        var targetLanguages = await GetLanguagesSetting<TargetLanguage>(SettingKeys.Translation.TargetLanguages);
        targetLanguages = FilterTargetLanguages(targetLanguages, targetLanguageFilter);
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
        
        _logger.LogInformation("Initiating manual subtitle processing for {FileName} (forceProcess={Force}, forceTranslation={ForceTrans}, forcePriority={Priority}).", media.FileName, forceProcess, forceTranslation, forcePriority);
        return await ProcessSubtitlesWithCount(
            media,
            mediaType,
            matchingSubtitles,
            sourceLanguages,
            targetLanguages,
            ignoreCaptions ?? "",
            forceTranslation,
            forceProcess,
            forcePriority,
            queueTranslations,
            maxTranslationsToQueue,
            integrityFindings);
        // return 0;
    }
    
    /// <summary>
    /// Processes subtitle files for translation and returns the count of translations queued.
    /// </summary>
    private async Task<int> ProcessSubtitlesWithCount(
        IMedia media,
        MediaType mediaType,
        List<Subtitles> subtitles,
        HashSet<string> sourceLanguages,
        HashSet<string> targetLanguages,
        string ignoreCaptions,
        bool forceTranslation = false,
        bool forceProcess = false,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null,
        ICollection<SubtitleIntegrityFinding>? integrityFindings = null)
    {
        var existingLanguages = ExtractLanguageCodes(subtitles);
        var knownForcedDialogueGeneratedPaths =
            await GetKnownForcedDialogueGeneratedSubtitlePathsAsync(media.Id, mediaType);
        var translationsQueued = 0;

        _logger.LogDebug(
            "ProcessSubtitlesWithCount: ExistingLanguages=[{Existing}], SourceLanguages=[{Sources}], TargetLanguages=[{Targets}], ForceTranslation={Force}",
            string.Join(", ", existingLanguages),
            string.Join(", ", sourceLanguages),
            string.Join(", ", targetLanguages),
            forceTranslation);

        var isAutoMode = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.SourceLanguageMode),
            "auto",
            StringComparison.OrdinalIgnoreCase);

        if ((!isAutoMode && sourceLanguages.Count == 0) || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Source or target languages are empty for counted subtitle processing. Auto mode: {IsAutoMode}. Source languages: {SourceCount}, Target languages: {TargetCount}",
                isAutoMode,
                sourceLanguages.Count,
                targetLanguages.Count);
            await UpdateHash();
            return 0;
        }

        ResolvedExternalSourceSubtitle? resolvedExternalSource;
        if (isAutoMode)
        {
            resolvedExternalSource = await _sourceSubtitleSnapshotService.ResolveExternalSourceWithAutoAsync(
                media,
                subtitles,
                true,
                targetLanguages.ToList());
        }
        else
        {
            resolvedExternalSource = await _sourceSubtitleSnapshotService.ResolveExternalSourceAsync(
                media,
                subtitles);
        }
        var resolvedSourceLanguage = resolvedExternalSource?.SourceLanguage;
        _logger.LogDebug("Source language match result: {SourceLanguage}", resolvedSourceLanguage ?? "NONE");

        if (resolvedExternalSource != null && targetLanguages.Any())
        {
            var sourceLanguage = resolvedExternalSource.SourceLanguage;
            var sourceSubtitle = resolvedExternalSource.Subtitle;

            if (sourceSubtitle != null &&
                !ExternalSubtitleCandidateHelper.ShouldSkipAsPrimarySource(sourceSubtitle))
            {
                var requestedRequiredOutputFormats =
                    await GetRequestedRequiredOutputFormatsAsync(sourceSubtitle.Format);
                var requiredOutputFormats =
                    SubtitleOutputModeHelper.DeserializeFormats(requestedRequiredOutputFormats);
                // When forceTranslation is true, translate to all target languages even if they exist
                var languagesToTranslate = forceTranslation 
                    ? targetLanguages.ToList()
                    : GetLanguagesMissingRequiredOutputFormats(
                            subtitles,
                            targetLanguages,
                            requiredOutputFormats,
                            knownGeneratedPrimaryTargetPaths: knownForcedDialogueGeneratedPaths)
                        .ToList();

                if (!forceTranslation)
                {
                    foreach (var targetLanguage in languagesToTranslate)
                    {
                        AddIntegrityFinding(
                            integrityFindings,
                            media,
                            mediaType,
                            sourceLanguage,
                            targetLanguage,
                            $"Missing required output format(s): {string.Join(", ", requiredOutputFormats)}.",
                            resolvedExternalSource.Snapshot,
                            sourceSubtitle.Path,
                            SelectMainTargetSubtitle(
                                subtitles,
                                targetLanguage,
                                knownForcedDialogueGeneratedPaths));
                    }
                }
                
                // Check integrity of existing target subtitles and add corrupt ones for re-translation
                var foundCorruption = false;
                if (!forceTranslation)
                {
                    var staleTargets = await _sourceSubtitleSnapshotService.GetStaleTargetLanguagesAsync(
                        media.Id,
                        mediaType,
                        targetLanguages,
                        resolvedExternalSource.Snapshot);

                    if (staleTargets.Count > 0)
                    {
                        foundCorruption = true;
                        languagesToTranslate = languagesToTranslate.Union(staleTargets).ToList();
                        foreach (var targetLanguage in staleTargets)
                        {
                            AddIntegrityFinding(
                                integrityFindings,
                                media,
                                mediaType,
                                sourceLanguage,
                                targetLanguage,
                                "Target subtitle was translated from an older or different selected source.",
                                resolvedExternalSource.Snapshot,
                                sourceSubtitle.Path,
                                SelectMainTargetSubtitle(
                                    subtitles,
                                    targetLanguage,
                                    knownForcedDialogueGeneratedPaths));
                        }

                        _logger.LogInformation(
                            "Detected stale target subtitles for {FileName}: {Targets}. Scheduling re-translation.",
                            media.FileName,
                            string.Join(", ", staleTargets));
                    }
                }

                if (!forceTranslation)
                {
                    var corruptLanguages = new List<string>();
                    foreach (var targetLang in targetLanguages.Intersect(existingLanguages))
                    {
                    var targetSubtitle = SelectMainTargetSubtitle(
                        subtitles,
                        targetLang,
                        knownForcedDialogueGeneratedPaths);
                        if (targetSubtitle != null)
                        {
                            var integrityResult = await _integrityService.ValidateIntegrityDetailedAsync(
                                sourceSubtitle.Path, 
                                targetSubtitle.Path);
                            if (!integrityResult.IsValid)
                            {
                                _logger.LogWarning(
                                    "Integrity check failed for {TargetLang} subtitle: {Path} - scheduling re-translation",
                                    targetLang, targetSubtitle.Path);
                                AddIntegrityFinding(
                                    integrityFindings,
                                    media,
                                    mediaType,
                                    sourceLanguage,
                                    targetLang,
                                    integrityResult.Reason,
                                    resolvedExternalSource.Snapshot,
                                    sourceSubtitle.Path,
                                    targetSubtitle,
                                    integrityResult);
                                corruptLanguages.Add(targetLang);
                            }
                        }
                    }
                    
                    if (corruptLanguages.Count > 0)
                    {
                        foundCorruption = true;
                    }
                    
                    // Add corrupt languages to the translation queue
                    languagesToTranslate = languagesToTranslate.Union(corruptLanguages).ToList();
                }
                
                if (ignoreCaptions == "true")
                {
                    var targetLanguagesWithCaptions = subtitles
                        .Where(s =>
                            targetLanguages.Contains(s.Language) &&
                            !string.IsNullOrEmpty(s.Caption) &&
                            !ShouldSkipAsMainTarget(s, knownForcedDialogueGeneratedPaths))
                        .Select(s => s.Language)
                        .Distinct()
                        .ToList();

                    if (targetLanguagesWithCaptions.Any())
                    {
                        _logger.LogInformation(
                            "Translation skipped because captions exist for target languages: |Green|{CaptionLanguages}|/Green|",
                            string.Join(", ", targetLanguagesWithCaptions));
                        if (!foundCorruption)
                        {
                            await UpdateHash();
                        }
                        return 0;
                    }
                }

                if (!queueTranslations)
                {
                    if (!foundCorruption && languagesToTranslate.Count == 0)
                    {
                        await UpdateHash();
                    }

                    _logger.LogInformation(
                        "Report-only subtitle processing for {FileName}: {Count} target(s) would be queued.",
                        media.FileName,
                        languagesToTranslate.Count);
                    return languagesToTranslate.Count;
                }

                var languagesToQueue = LimitQueuedLanguages(languagesToTranslate, maxTranslationsToQueue);
                foreach (var targetLanguage in languagesToQueue)
                {
                    if (await HasActiveRequestAsync(
                            media.Id,
                            mediaType,
                            sourceLanguage,
                            targetLanguage,
                            requestedRequiredOutputFormats,
                            SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(sourceSubtitle.Path),
                            null,
                            resolvedExternalSource.Snapshot.Identity))
                    {
                        _logger.LogInformation(
                            "Skipping enqueue for {FileName} {Source}->{Target}: translation request already active.",
                            media.FileName,
                            sourceLanguage,
                            targetLanguage);
                        MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                        continue;
                    }
                    if (!forcePriority &&
                        await _translationRequestService.HasExistingNonSupplementalRequestAsync(
                            media.Id, mediaType, sourceLanguage, targetLanguage))
                    {
                        _logger.LogInformation(
                            "Skipping enqueue for {FileName} {Source}->{Target}: completed translation request already exists and forcePriority is not set.",
                            media.FileName, sourceLanguage, targetLanguage);
                        MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                        continue;
                    }



                    await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                    {
                        MediaId = media.Id,
                        MediaType = mediaType,
                        SubtitlePath = sourceSubtitle.Path,
                        TargetLanguage = targetLanguage,
                        SourceLanguage = sourceLanguage,
                        SubtitleFormat = sourceSubtitle.Format,
                        SourceSubtitleType = SubtitleLanguageHelper.DetermineSubtitleTypeFromFilename(sourceSubtitle.Path),
                        SourceSnapshot = resolvedExternalSource.Snapshot
                    }, forcePriority);
                    MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                    translationsQueued++;
                    _logger.LogInformation(
                        "Initiating translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{subtitleFile}|/Green|",
                        sourceLanguage,
                        targetLanguage,
                        sourceSubtitle.Path);
                }

                // Only update hash if no corruption was found - ensures re-validation if translation fails
                if (!foundCorruption)
                {
                    await UpdateHash();
                }
                else
                {
                    _logger.LogDebug("Skipping hash update for {FileName} due to corruption found - will re-validate next run", media.FileName);
                }
                return translationsQueued;
            }

            _logger.LogWarning("No source subtitle file found for language: |Green|{SourceLanguage}|/Green|",
                sourceLanguage);

            _logger.LogInformation(
                "No external source subtitle found for {FileName}. Checking for embedded subtitles...",
                media.FileName);
        }

        // Final fallback: try embedded
        return await TryQueueEmbeddedSubtitleTranslation(
            media,
            mediaType,
            forceTranslation,
            forceProcess,
            forcePriority,
            queueTranslations,
            maxTranslationsToQueue,
            integrityFindings);
    }
    
    /// <summary>
    /// Attempts to queue translation jobs for media with embedded subtitles but no external subtitles.
    /// </summary>
    /// <param name="media">The media item to process</param>
    /// <param name="mediaType">The type of media (Movie or Episode)</param>
    /// <param name="forceTranslation">If true, translates to all target languages even if they already exist.</param>
    /// <param name="forceProcess">If true, bypasses the media hash check</param>
    /// <param name="forcePriority">If true, forces jobs to use the priority queue</param>
    /// <param name="queueTranslations">If false, reports queueable translations without creating requests.</param>
    /// <param name="maxTranslationsToQueue">Optional maximum number of requests to create.</param>
    /// <param name="integrityFindings">Optional collection that receives detailed integrity findings.</param>
    /// <returns>The number of translation requests queued</returns>
    private async Task<int> TryQueueEmbeddedSubtitleTranslation(
        IMedia media,
        MediaType mediaType,
        bool forceTranslation,
        bool forceProcess,
        bool forcePriority = false,
        bool queueTranslations = true,
        int? maxTranslationsToQueue = null,
        ICollection<SubtitleIntegrityFinding>? integrityFindings = null)
    {
        if (media.Path == null)
        {
            return 0;
        }

        // Preserve the order of configured source languages so we can treat
        // them as a priority list (e.g. [en, ja] => prefer English when both
        // are good candidates, but fall back to Japanese when English only
        // has "Signs & Songs" style tracks).
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

        var isAutoMode = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.SourceLanguageMode),
            "auto",
            StringComparison.OrdinalIgnoreCase);

        if ((!isAutoMode && configuredSourceLanguages.Count == 0) || targetLanguages.Count == 0)
        {
            _logger.LogWarning(
                "Cannot queue embedded subtitle translation for {FileName}: source or target languages not configured. Auto mode: {IsAutoMode}",
                media.FileName,
                isAutoMode);
            return 0;
        }
        
        // ============================================================================
        // OPTIMISTIC SKIP: Check if we already have cached embedded subtitle data
        // from the sync job. If hash matches, skip the expensive ffprobe call entirely.
        // This is the key optimization that prevents the automation job from scanning
        // all media files every run.
        // ============================================================================
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
            
            // If we have cached data AND media was indexed, use optimistic hash check
            var indexedAt = cachedMovie?.IndexedAt ?? cachedEpisode?.IndexedAt;
            if (cachedEmbedded != null && indexedAt != null)
            {
                var optimisticHash = CreateEmbeddedHash(cachedEmbedded, configuredSourceLanguages, targetLanguages);
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
        // ============================================================================
        
        // Sync embedded subtitles from the media file
        List<EmbeddedSubtitle>? embeddedSubtitles = null;
        IMedia? trackedMedia = null;
        
        if (mediaType == MediaType.Episode)
        {
            // Don't use Include here - we're syncing immediately after, and Include+ExecuteDeleteAsync
            // causes duplication because ExecuteDeleteAsync bypasses the change tracker
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(e => e.Id == media.Id);
                
            if (episode != null)
            {
                // Force sync to refresh embedded subtitles
                await _extractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                embeddedSubtitles = episode.EmbeddedSubtitles;
                trackedMedia = episode;
            }
        }
        else if (mediaType == MediaType.Movie)
        {
            // Don't use Include here - we're syncing immediately after, and Include+ExecuteDeleteAsync
            // causes duplication because ExecuteDeleteAsync bypasses the change tracker
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(m => m.Id == media.Id);
                
            if (movie != null)
            {
                // Force sync to refresh embedded subtitles
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

            // Update hash so we don't retry constantly unless streams or settings change
            _media = trackedMedia ?? media;
            _mediaType = mediaType;
            _hash = CreateEmbeddedHash([], configuredSourceLanguages, targetLanguages);
            await UpdateHash();

            return 0;
        }

        var mediaForHash = trackedMedia ?? media;

        // Compute embedded hash based on current streams and settings
        var embeddedHash = CreateEmbeddedHash(embeddedSubtitles, configuredSourceLanguages, targetLanguages);

        if (!forceProcess && !string.IsNullOrEmpty(mediaForHash.MediaHash) && mediaForHash.MediaHash == embeddedHash)
        {
            _logger.LogDebug("Skipping embedded translation for {FileName}: hash matches and not forcing", media.FileName);
            return 0;
        }

        _media = mediaForHash;
        _mediaType = mediaType;
        _hash = embeddedHash;
        
        _logger.LogInformation(
            "Found {Count} embedded subtitles for {FileName}: [{Subtitles}]",
            embeddedSubtitles.Count, media.FileName,
            string.Join(", ", embeddedSubtitles.Select(s => $"{s.Language ?? "unknown"}:{s.CodecName}")));

        var readableEmbeddedSubs = embeddedSubtitles.Where(s => s.IsReadableSource()).ToList();
        if (readableEmbeddedSubs.Count == 0)
        {
            var queuedOcr = await TryQueueEmbeddedSubtitleOcrAsync(
                media,
                mediaType,
                embeddedSubtitles,
                configuredSourceLanguages,
                targetLanguages);
            if (queuedOcr)
            {
                return 0;
            }

            // Check if OCR has already completed successfully on any embedded subtitle stream.
            // When OCR status is Succeeded or Approved, the extracted SRT file should be used
            // as the translation source rather than re-queuing OCR.
            var ocrCompleted = embeddedSubtitles
                .FirstOrDefault(s => !s.IsTextBased
                    && _subtitleOcrService.IsSupportedCodec(s.CodecName)
                    && s.OcrStatus is SubtitleOcrStatus.Succeeded or SubtitleOcrStatus.Approved
                    && !string.IsNullOrWhiteSpace(s.OcrExtractedPath)
                    && File.Exists(s.OcrExtractedPath));

            if (ocrCompleted != null)
            {
                _logger.LogInformation(
                    "OCR already completed for {FileName} stream {StreamIndex}. Queuing translation from OCR output at {OcrPath}.",
                    media.FileName,
                    ocrCompleted.StreamIndex,
                    ocrCompleted.OcrExtractedPath);

                var ocrSubtitles = new List<Subtitles>
                {
                    new()
                    {
                        Path = ocrCompleted.OcrExtractedPath,
                        FileName = Path.GetFileName(ocrCompleted.OcrExtractedPath),
                        Language = SubtitleLanguageHelper.NormalizeLanguageCode(
                            ocrCompleted.Language ?? "eng") ?? "eng",
                        Format = ".srt"
                    }
                };

                var ignoreCaptionsVal =
                    await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions) ?? "";

                return await ProcessSubtitlesWithCount(
                    media,
                    mediaType,
                    ocrSubtitles,
                    [..configuredSourceLanguages],
                    targetLanguages,
                    ignoreCaptionsVal,
                    forceTranslation,
                    forceProcess,
                    forcePriority,
                    queueTranslations,
                    maxTranslationsToQueue,
                    integrityFindings);
            }

            _logger.LogWarning(
                "No readable embedded subtitles found for {FileName}. Only unsupported or unprocessed image-based subtitles are available.",
                media.FileName);
            await UpdateHash();
            return 0;
        }

        var ignoreCaptionsSetting = await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions);

        var sourceSelection = await _subtitleSourceSelectionService.SelectPrimaryAsync(
            readableEmbeddedSubs,
            configuredSourceLanguages,
            allowCaptionFallback: !string.Equals(ignoreCaptionsSetting, "true", StringComparison.OrdinalIgnoreCase),
            targetLanguages: isAutoMode ? targetLanguages.ToList() : null);

        if (sourceSelection.SelectedSubtitle == null)
        {
            var availableLanguages = readableEmbeddedSubs
                .GroupBy(s => SubtitleLanguageHelper.NormalizeLanguageCode(s.Language))
                .Select(g => g.Key ?? "unknown")
                .Distinct()
                .ToList();

            if (isAutoMode)
            {
                _logger.LogWarning(
                    "Auto mode: no usable embedded subtitle found for {FileName}. " +
                    "All candidate languages scored below minimum threshold. " +
                    "Available embedded subtitle languages: [{Available}].",
                    media.FileName,
                    string.Join(", ", availableLanguages));
            }
            else
            {
                _logger.LogWarning(
                    "No usable full-dialogue embedded subtitle matches configured source languages [{Sources}] for {FileName}. " +
                    "Available embedded subtitle languages: [{Available}]. Candidate assessment: [{Assessments}]. " +
                    "Update your source languages on the Services page if you want to translate from one of these.",
                    string.Join(", ", configuredSourceLanguages),
                    media.FileName,
                    string.Join(", ", availableLanguages),
                    string.Join("; ", sourceSelection.Assessments.Select(assessment =>
                        $"stream={assessment.Subtitle.StreamIndex}, role={assessment.Role}, reason={assessment.Reason}")));
            }

            if (_diagnosticsService != null)
            {
                try
                {
                    await _diagnosticsService.RecordAsync(
                        new TranslationDiagnosticEventRequest
                        {
                            MediaId = media.Id,
                            MediaType = _mediaType,
                            Title = media.FileName,
                            Stage = "source_selection",
                            ReasonCode = "no_usable_source",
                            Summary = isAutoMode
                                ? $"Auto mode: no embedded subtitle scored high enough. Available: [{string.Join(", ", availableLanguages)}]."
                                : $"No usable full-dialogue source for languages [{string.Join(", ", configuredSourceLanguages)}]. Available: [{string.Join(", ", availableLanguages)}].",
                            SampleLines = sourceSelection.Assessments.Select(a =>
                                $"stream={a.Subtitle.StreamIndex}: role={a.Role}, lang={a.Subtitle.Language ?? "?"}, title=\"{a.Subtitle.Title ?? ""}\", reason={a.Reason}").ToList(),
                            DetailsJson = System.Text.Json.JsonSerializer.Serialize(sourceSelection.Assessments.Select(a => new
                            {
                                a.Subtitle.StreamIndex,
                                a.Subtitle.Language,
                                a.Subtitle.Title,
                                a.Subtitle.CodecName,
                                a.Subtitle.IsForced,
                                a.Role,
                                a.Score,
                                a.EntryCount,
                                a.Reason
                            }))
                        },
                        CancellationToken.None);
                }
                catch (Exception diagEx)
                {
                    _logger.LogDebug(diagEx, "Failed to record source_selection diagnostic event.");
                }
            }

            await UpdateHash();
            return 0;
        }

        var selectedSubtitle = sourceSelection.SelectedSubtitle;
        var selectedSourceLanguage = sourceSelection.MatchedLanguage;
        var selectedSourceType = SubtitleLanguageHelper.DetermineSubtitleType(selectedSubtitle);

        _logger.LogInformation(
            "Selected embedded subtitle for translation: StreamIndex={StreamIndex}, LanguageTag={LanguageTag}, ConfiguredLanguage={ConfiguredLanguage}, Role={Role}, Title=\"{Title}\", Codec={Codec}",
            selectedSubtitle.StreamIndex,
            selectedSubtitle.Language ?? "unknown",
            selectedSourceLanguage,
            sourceSelection.SelectedRole,
            selectedSubtitle.Title ?? "<none>",
            selectedSubtitle.CodecName);

        // Get external subtitles to check which target languages already exist and validate them
        var allExternalSubtitles = await _subtitleService.GetAllSubtitles(media.Path!);
        var knownGeneratedPaths = await GetKnownGeneratedSubtitlePathsAsync(media.Id, mediaType);
        var knownForcedDialogueGeneratedPaths =
            await GetKnownForcedDialogueGeneratedSubtitlePathsAsync(media.Id, mediaType);
        var matchingExternalSubtitles = MediaSubtitleMatcher.FilterMatchingSubtitles(
            media.FileName,
            allExternalSubtitles,
            knownGeneratedPaths);
        var existingExternalLanguages = matchingExternalSubtitles
            .Where(subtitle => !ShouldSkipAsMainTarget(subtitle, knownForcedDialogueGeneratedPaths))
            .Select(s => s.Language.ToLowerInvariant())
            .ToHashSet();

        // Check for embedded target language subtitles that should skip translation
        var skipWhenTargetEmbedded = await _settingService.GetSetting(
            SettingKeys.SubtitleValidation.SkipWhenTargetEmbedded) ?? "true";
        var qualifyingEmbeddedTargets = new List<Subtitles>();
        
        if (skipWhenTargetEmbedded.Equals("true", StringComparison.OrdinalIgnoreCase) && !forceTranslation)
        {
            foreach (var subtitle in readableEmbeddedSubs.Where(subtitle => subtitle.IsTextBased))
            {
                if (string.IsNullOrWhiteSpace(subtitle.Language))
                {
                    continue;
                }

                // Check if this embedded subtitle matches any target language
                foreach (var targetLanguage in targetLanguages)
                {
                    if (SubtitleLanguageHelper.LanguageMatches(subtitle.Language, targetLanguage))
                    {
                        // Use score-based quality check: score >= 30 means it's likely a full dialogue track
                        // Lower scores indicate sparse/signs-only tracks (heuristic from title, forced flag, etc.)
                        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, targetLanguage);
                        if (score >= 30)
                        {
                            var embeddedFormat = SubtitleOutputModeHelper.NormalizeFormat(subtitle.CodecName);
                            if (!string.IsNullOrWhiteSpace(embeddedFormat))
                            {
                                qualifyingEmbeddedTargets.Add(new Subtitles
                                {
                                    FileName = media.FileName ?? string.Empty,
                                    Language = targetLanguage,
                                    Format = embeddedFormat
                                });
                            }

                            _logger.LogInformation(
                                "Found embedded target subtitle for {FileName}: Language={Language}, StreamIndex={StreamIndex}, Score={Score}. Counting it toward existing target outputs.",
                                media.FileName,
                                targetLanguage,
                                subtitle.StreamIndex,
                                score);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Embedded target subtitle for {FileName} (Language={Language}) has low score ({Score}), likely sparse. Will still translate.",
                                media.FileName,
                                targetLanguage,
                                score);
                        }
                        break;
                    }
                }
            }
        }

        var requestedRequiredOutputFormats =
            await GetRequestedRequiredOutputFormatsAsync(selectedSubtitle.GetReadableSourceFormat());
        var requiredOutputFormats =
            SubtitleOutputModeHelper.DeserializeFormats(requestedRequiredOutputFormats);
        var existingTargetSubtitles = matchingExternalSubtitles
            .Concat(qualifyingEmbeddedTargets)
            .ToList();
        var selectedSourceSnapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(
            selectedSubtitle,
            selectedSourceLanguage);
        var languagesMissingRequiredFormats = GetLanguagesMissingRequiredOutputFormats(
            existingTargetSubtitles,
            targetLanguages,
            requiredOutputFormats,
            knownGeneratedPrimaryTargetPaths: knownForcedDialogueGeneratedPaths);

        // Determine which languages need translation (missing or corrupt).
        var languagesToTranslate = forceTranslation
            ? targetLanguages.ToList()
            : targetLanguages
                .Where(targetLanguage =>
                    languagesMissingRequiredFormats.Contains(
                        SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage)))
                .ToList();

        if (!forceTranslation)
        {
            foreach (var targetLanguage in languagesToTranslate)
            {
                AddIntegrityFinding(
                    integrityFindings,
                    media,
                    mediaType,
                    selectedSourceLanguage,
                    targetLanguage,
                    $"Missing required output format(s): {string.Join(", ", requiredOutputFormats)}.",
                    selectedSourceSnapshot,
                    null,
                    SelectMainTargetSubtitle(
                        matchingExternalSubtitles,
                        targetLanguage,
                        knownForcedDialogueGeneratedPaths));
            }
        }

        // For integrity validation (forceTranslation=false), we need to extract temp source and check existing targets
        string? tempSourcePath = null;
        var deleteTempSourcePath = false;
        var foundCorruption = false;

        if (!forceTranslation)
        {
            var staleTargets = await _sourceSubtitleSnapshotService.GetStaleTargetLanguagesAsync(
                media.Id,
                mediaType,
                targetLanguages,
                selectedSourceSnapshot);

            if (staleTargets.Count > 0)
            {
                foundCorruption = true;
                languagesToTranslate = languagesToTranslate.Union(staleTargets).ToList();
                foreach (var targetLanguage in staleTargets)
                {
                    AddIntegrityFinding(
                        integrityFindings,
                        media,
                        mediaType,
                        selectedSourceLanguage,
                        targetLanguage,
                        "Target subtitle was translated from an older or different selected embedded source.",
                        selectedSourceSnapshot,
                        null,
                        SelectMainTargetSubtitle(
                            matchingExternalSubtitles,
                            targetLanguage,
                            knownForcedDialogueGeneratedPaths));
                }

                _logger.LogInformation(
                    "Detected stale embedded target subtitles for {FileName}: {Targets}. Scheduling re-translation.",
                    media.FileName,
                    string.Join(", ", staleTargets));
            }
        }

        try
        {
            // Debug logging to trace validation check
            _logger.LogInformation(
                "Validation check - forceTranslation={ForceTranslation}, matchingExternalSubtitles=[{Subtitles}], existingExternalLanguages=[{ExistingLangs}], targetLanguages=[{TargetLangs}]",
                forceTranslation,
                string.Join(", ", matchingExternalSubtitles.Select(s => $"{s.FileName}:{s.Language}")),
                string.Join(", ", existingExternalLanguages),
                string.Join(", ", targetLanguages));
            
            var hasMatchingTarget = existingExternalLanguages.Any(lang => targetLanguages.Contains(lang));
            _logger.LogInformation("Validation gate check: !forceTranslation={NotForce}, hasMatchingTarget={HasMatch}, willValidate={WillValidate}",
                !forceTranslation, hasMatchingTarget, !forceTranslation && hasMatchingTarget);
                
            if (!forceTranslation && hasMatchingTarget)
            {
                if (selectedSubtitle.HasUsableOcr())
                {
                    tempSourcePath = selectedSubtitle.OcrExtractedPath;
                }
                else
                {
                    var tempDir = Path.GetTempPath();
                    tempSourcePath = await _extractionService.ExtractSubtitle(
                        Path.Combine(media.Path!, media.FileName!),
                        selectedSubtitle.StreamIndex,
                        tempDir,
                        selectedSubtitle.CodecName,
                        selectedSourceLanguage);
                    deleteTempSourcePath = tempSourcePath != null;
                }

                if (tempSourcePath != null)
                {
                    var corruptLanguages = new List<string>();
                    foreach (var targetLang in targetLanguages.Intersect(existingExternalLanguages))
                    {
                        var targetSubtitle = SelectMainTargetSubtitle(
                            matchingExternalSubtitles,
                            targetLang,
                            knownForcedDialogueGeneratedPaths);
                        if (targetSubtitle != null)
                        {
                            var integrityResult = await _integrityService.ValidateIntegrityDetailedAsync(
                                tempSourcePath,
                                targetSubtitle.Path);
                            if (!integrityResult.IsValid)
                            {
                                _logger.LogWarning(
                                    "Integrity check failed for {TargetLang} subtitle: {Path} - scheduling re-translation (embedded source)",
                                    targetLang, targetSubtitle.Path);
                                AddIntegrityFinding(
                                    integrityFindings,
                                    media,
                                    mediaType,
                                    selectedSourceLanguage,
                                    targetLang,
                                    integrityResult.Reason,
                                    selectedSourceSnapshot,
                                    null,
                                    targetSubtitle,
                                    integrityResult);
                                corruptLanguages.Add(targetLang);
                            }
                        }
                    }

                    if (corruptLanguages.Count > 0)
                    {
                        foundCorruption = true;
                    }

                    // Add corrupt languages to the translation queue
                    languagesToTranslate = languagesToTranslate.Union(corruptLanguages).ToList();
                }
            }

            if (!queueTranslations)
            {
                if (!foundCorruption && languagesToTranslate.Count == 0)
                {
                    await UpdateHash();
                }

                _logger.LogInformation(
                    "Report-only embedded subtitle processing for {FileName}: {Count} target(s) would be queued.",
                    media.FileName,
                    languagesToTranslate.Count);
                return languagesToTranslate.Count;
            }

            // Create translation requests for each target language (with empty subtitle path - TranslationJob will extract)
            var translationsQueued = 0;
            var sourceSnapshot = selectedSourceSnapshot;
            var languagesToQueue = LimitQueuedLanguages(languagesToTranslate, maxTranslationsToQueue);
            foreach (var targetLanguage in languagesToQueue)
            {
                if (await HasActiveRequestAsync(
                        media.Id,
                        mediaType,
                        selectedSourceLanguage,
                        targetLanguage,
                        requestedRequiredOutputFormats,
                        selectedSourceType,
                        sourceSnapshot.StreamIndex,
                        sourceSnapshot.Identity))
                {
                    _logger.LogInformation(
                        "Skipping embedded enqueue for {FileName} {Source}->{Target}: translation request already active.",
                        media.FileName,
                        selectedSourceLanguage,
                        targetLanguage);
                    MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                    continue;
                }
                if (!forcePriority &&
                    await _translationRequestService.HasExistingNonSupplementalRequestAsync(
                        media.Id, mediaType, selectedSourceLanguage, targetLanguage))
                {
                    _logger.LogInformation(
                        "Skipping embedded enqueue for {FileName} {Source}->{Target}: completed translation request already exists and forcePriority is not set.",
                        media.FileName, selectedSourceLanguage, targetLanguage);
                    MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                    continue;
                }



                await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                {
                    MediaId = media.Id,
                    MediaType = mediaType,
                    SubtitlePath = null, // Will trigger embedded extraction in TranslationJob
                    TargetLanguage = targetLanguage,
                    SourceLanguage = selectedSourceLanguage,
                    SubtitleFormat = selectedSubtitle.GetReadableSourceFormat(),
                    SourceSubtitleType = selectedSourceType,
                    SelectedStreamTitle = selectedSubtitle.Title,
                    IsForcedSubtitle = selectedSubtitle.IsForced,
                    SourceSnapshot = sourceSnapshot
                }, forcePriority);
                MarkIntegrityFindingQueued(integrityFindings, media, mediaType, targetLanguage);
                translationsQueued++;
                _logger.LogInformation(
                    "Queued embedded subtitle translation from |Orange|{sourceLanguage}|/Orange| to |Orange|{targetLanguage}|/Orange| for |Green|{FileName}|/Green|",
                    selectedSourceLanguage,
                    targetLanguage,
                    media.FileName);
            }

            translationsQueued += await TryQueueSupplementalEmbeddedTranslationsAsync(
                media,
                mediaType,
                sourceSelection,
                matchingExternalSubtitles,
                targetLanguages,
                forceTranslation,
                forcePriority,
                maxTranslationsToQueue.HasValue
                    ? Math.Max(0, maxTranslationsToQueue.Value - translationsQueued)
                    : null);

            // Only update hash if no corruption was found - this ensures re-validation on next run
            // if translation job fails or app crashes before completing
            if (!foundCorruption)
            {
                await UpdateHash();
            }
            else
            {
                _logger.LogDebug("Skipping hash update for {FileName} due to corruption found - will re-validate next run", media.FileName);
            }
            return translationsQueued;
        }
        finally
        {
            if (deleteTempSourcePath && tempSourcePath != null && File.Exists(tempSourcePath))
            {
                try
                {
                    File.Delete(tempSourcePath);
                    _logger.LogDebug("Deleted temporary validation subtitle: {TempPath}", tempSourcePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary validation subtitle: {TempPath}", tempSourcePath);
                }
            }
        }
	    }

    private async Task<bool> TryQueueEmbeddedSubtitleOcrAsync(
        IMedia media,
        MediaType mediaType,
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> configuredSourceLanguages,
        IReadOnlyCollection<string> targetLanguages)
    {
        if (_subtitleOcrService == null || targetLanguages.Count == 0)
        {
            return false;
        }

        var ocrEnabled = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled) ?? "true",
            "true",
            StringComparison.OrdinalIgnoreCase);
        var autoQueue = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue) ?? "true",
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (!ocrEnabled || !autoQueue)
        {
            return false;
        }

        var ignoreCaptions = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.IgnoreCaptions),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var isAutoMode = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.SourceLanguageMode),
            "auto",
            StringComparison.OrdinalIgnoreCase);
        if (!isAutoMode && configuredSourceLanguages.Count == 0)
        {
            return false;
        }

        var candidate = embeddedSubtitles
            .Where(subtitle => !subtitle.IsTextBased)
            .Where(subtitle => _subtitleOcrService.IsSupportedCodec(subtitle.CodecName))
            .Where(subtitle => subtitle.OcrStatus is SubtitleOcrStatus.NotStarted
                or SubtitleOcrStatus.Queued
                or SubtitleOcrStatus.Processing)
            .Where(subtitle => isAutoMode ||
                configuredSourceLanguages.Any(language =>
                    SubtitleLanguageHelper.LanguageMatches(subtitle.Language, language)))
            .Where(subtitle => !ignoreCaptions ||
                               !SubtitleLanguageHelper.IsCaptionSubtitleType(
                                   SubtitleLanguageHelper.DetermineSubtitleType(subtitle)))
            .Where(subtitle => !SubtitleLanguageHelper.IsSupplementalSubtitleType(
                SubtitleLanguageHelper.DetermineSubtitleType(subtitle)))
            .OrderByDescending(subtitle => isAutoMode
                ? SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, subtitle.Language)
                : configuredSourceLanguages.Max(language =>
                    SubtitleLanguageHelper.ScoreSubtitleCandidate(subtitle, language)))
            .ThenBy(subtitle => subtitle.StreamIndex)
            .FirstOrDefault();

        if (candidate == null)
        {
            return false;
        }

        if (SubtitleOcrStatePolicy.IsStaleTransient(candidate, DateTime.UtcNow))
        {
            if (SubtitleOcrJobActivity.HasActiveJob(media.Id, mediaType, candidate.StreamIndex))
            {
                _logger.LogInformation(
                    "OCR is stale by timestamp but still active in Hangfire for {FileName} stream {StreamIndex}; waiting for the active job.",
                    media.FileName,
                    candidate.StreamIndex);
                return true;
            }

            _logger.LogWarning(
                "Resetting stale OCR {Status} state for {FileName} stream {StreamIndex}; last attempt was {AttemptedAt}.",
                candidate.OcrStatus,
                media.FileName,
                candidate.StreamIndex,
                candidate.OcrAttemptedAt);
            SubtitleOcrStatePolicy.ResetStaleTransient(candidate);
            await _dbContext.SaveChangesAsync();
        }

        if (candidate.OcrStatus is SubtitleOcrStatus.Queued or SubtitleOcrStatus.Processing)
        {
            _logger.LogInformation(
                "OCR is already {Status} for {FileName} stream {StreamIndex}; waiting for the next automation pass.",
                candidate.OcrStatus,
                media.FileName,
                candidate.StreamIndex);
            return true;
        }

        var result = await _subtitleOcrService.QueueOcrAsync(
            media.Id,
            mediaType,
            candidate.StreamIndex,
            manual: false);
        if (!result.Success)
        {
            _logger.LogWarning(
                "Could not queue OCR for {FileName} stream {StreamIndex}: {Error}",
                media.FileName,
                candidate.StreamIndex,
                result.Error);
            return false;
        }

        if (_backgroundJobClient != null)
        {
            _backgroundJobClient.Enqueue<SubtitleOcrJob>(job => job.Execute(
                media.Id,
                mediaType,
                candidate.StreamIndex,
                false));
        }
        else
        {
            BackgroundJob.Enqueue<SubtitleOcrJob>(job => job.Execute(
                media.Id,
                mediaType,
                candidate.StreamIndex,
                false));
        }
        _logger.LogInformation(
            "Queued subtitle OCR for {FileName} stream {StreamIndex}. Translation will be reconsidered after OCR quality checks pass.",
            media.FileName,
            candidate.StreamIndex);
        return true;
    }

    private static void AddIntegrityFinding(
        ICollection<SubtitleIntegrityFinding>? findings,
        IMedia media,
        MediaType mediaType,
        string sourceLanguage,
        string targetLanguage,
        string reason,
        SourceSubtitleSnapshot? sourceSnapshot = null,
        string? sourcePath = null,
        Subtitles? targetSubtitle = null,
        SubtitleIntegrityCheckResult? integrityResult = null)
    {
        if (findings == null)
        {
            return;
        }

        var normalizedTarget = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        var targetPath = targetSubtitle?.Path;
        if (findings.Any(f =>
                f.MediaId == media.Id &&
                string.Equals(f.MediaType, mediaType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.TargetLanguage, normalizedTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.TargetPath ?? string.Empty, targetPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Reason, reason, StringComparison.Ordinal)))
        {
            return;
        }

        findings.Add(new SubtitleIntegrityFinding
        {
            MediaId = media.Id,
            MediaType = mediaType.ToString(),
            MediaTitle = media.Title,
            SourceLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(sourceLanguage),
            TargetLanguage = normalizedTarget,
            SourceRole = "primary",
            Reason = reason,
            SourcePath = sourcePath ?? sourceSnapshot?.SourcePath,
            TargetPath = targetPath,
            SourceEntries = integrityResult?.SourceEntryCount,
            TargetEntries = integrityResult?.TargetEntryCount,
            MinimumTargetEntries = integrityResult?.MinimumTargetEntryCount,
            SourceSnapshotType = sourceSnapshot?.SourceType,
            SourceSnapshotIdentity = sourceSnapshot?.Identity,
            SourceSnapshotStreamIndex = sourceSnapshot?.StreamIndex
        });
    }

    private static void MarkIntegrityFindingQueued(
        ICollection<SubtitleIntegrityFinding>? findings,
        IMedia media,
        MediaType mediaType,
        string targetLanguage)
    {
        if (findings == null)
        {
            return;
        }

        var normalizedTarget = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        foreach (var finding in findings.Where(f =>
                     f.MediaId == media.Id &&
                     string.Equals(f.MediaType, mediaType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(f.TargetLanguage, normalizedTarget, StringComparison.OrdinalIgnoreCase)))
        {
            finding.IsQueued = true;
        }
    }

	    /// <summary>
    /// Probes and retrieves embedded subtitles for the currently processing media.
    /// Ensures the database is synced with the file's current state.
    /// </summary>
    private async Task<List<EmbeddedSubtitle>?> ProbeEmbeddedSubtitlesForCurrentMedia()
    {
        if (_media == null) return null;

        if (_mediaType == MediaType.Episode)
        {
            // Don't use Include here - we're syncing immediately after, and Include+ExecuteDeleteAsync
            // causes duplication because ExecuteDeleteAsync bypasses the change tracker
            var episode = await _dbContext.Episodes
                .FirstOrDefaultAsync(e => e.Id == _media.Id);
                
            if (episode != null)
            {
                await _extractionService.SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                return episode.EmbeddedSubtitles;
            }
        }
        else if (_mediaType == MediaType.Movie)
        {
            // Don't use Include here - we're syncing immediately after, and Include+ExecuteDeleteAsync
            // causes duplication because ExecuteDeleteAsync bypasses the change tracker
            var movie = await _dbContext.Movies
                .FirstOrDefaultAsync(m => m.Id == _media.Id);
                
            if (movie != null)
            {
                await _extractionService.SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                return movie.EmbeddedSubtitles;
            }
        }
        
        return null;
    }

    private async Task<bool> IsCorruptExternalSourceAsync(Subtitles subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle.Path) || !File.Exists(subtitle.Path))
        {
            return false;
        }

        try
        {
            var subtitles = await _subtitleService.ReadSubtitles(subtitle.Path);
            var health = SubtitleSourceHealthAnalyzer.Analyze(subtitles);
            return health.Status == SubtitleSourceHealthStatus.CorruptText;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to analyze external subtitle source health for {Path}. Continuing with selected source.",
                subtitle.Path);
            return false;
        }
    }


    private async Task<bool> HasActiveRequestAsync(
        int mediaId,
        MediaType mediaType,
        string sourceLanguage,
        string targetLanguage,
        string requestedRequiredOutputFormats,
        string? sourceSubtitleType = null,
        int? sourceSnapshotStreamIndex = null,
        string? sourceSnapshotIdentity = null)
    {
        var workloadItemKey = $"library:{mediaType}:{mediaId}";
        var sourceDedupeKey = TranslationRequestService.BuildSourceDedupeKey(
            sourceSubtitleType,
            false,
            sourceSnapshotIdentity,
            sourceSnapshotStreamIndex,
            null);
        var isSupplemental = SubtitleLanguageHelper.IsSupplementalSubtitleType(sourceSubtitleType);
        var hasSourceType = !string.IsNullOrWhiteSpace(sourceSubtitleType);
        var hasSourceIdentity = !string.IsNullOrWhiteSpace(sourceSnapshotIdentity);
        var query = _dbContext.TranslationRequests
            .Where(tr =>
                (tr.WorkloadItemKey == workloadItemKey ||
                 ((tr.WorkloadItemKey == string.Empty || tr.WorkloadItemKey == null) &&
                  tr.WorkloadKind == TranslationWorkloadKind.Library &&
                    tr.MediaId == mediaId &&
                    tr.MediaType == mediaType)) &&
                tr.SourceLanguage == sourceLanguage &&
                tr.TargetLanguage == targetLanguage &&
                tr.SourceDedupeKey == sourceDedupeKey &&
(tr.IsActive == true || tr.Status == TranslationStatus.Failed));

query = isSupplemental
            ? query.Where(tr =>
                (tr.SourceSubtitleType == SubtitleLanguageHelper.TypeForced ||
                 tr.SourceSubtitleType == SubtitleLanguageHelper.TypeSignsSongs) &&
                (!hasSourceType ||
                 tr.SourceSubtitleType == sourceSubtitleType) &&
                (!sourceSnapshotStreamIndex.HasValue ||
                 tr.SourceSnapshotStreamIndex == sourceSnapshotStreamIndex) &&
                (!hasSourceIdentity ||
                 tr.SourceSnapshotIdentity == sourceSnapshotIdentity))
            : query.Where(tr =>
                tr.SourceSubtitleType != SubtitleLanguageHelper.TypeForced &&
                tr.SourceSubtitleType != SubtitleLanguageHelper.TypeSignsSongs);

        return await query.AnyAsync();
    }

    private async Task<HashSet<string>> GetKnownGeneratedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType)
    {
        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .Where(request => request.GeneratedSubtitlePaths != null && request.GeneratedSubtitlePaths != string.Empty)
            .ToListAsync();

        return MediaSubtitleMatcher.ExtractGeneratedPaths(requests);
    }

    private async Task<HashSet<string>> GetKnownForcedDialogueGeneratedSubtitlePathsAsync(
        int mediaId,
        MediaType mediaType)
    {
        var requests = await _dbContext.TranslationRequests
            .AsNoTracking()
            .Where(request => request.WorkloadKind == TranslationWorkloadKind.Library)
            .Where(request => request.MediaId == mediaId && request.MediaType == mediaType)
            .Where(request => request.Status == TranslationStatus.Completed)
            .Where(request => request.SourceSubtitleType == SubtitleLanguageHelper.TypeForcedDialogue)
            .Where(request =>
                (request.GeneratedSubtitlePaths != null && request.GeneratedSubtitlePaths != string.Empty) ||
                (request.TranslatedSubtitle != null && request.TranslatedSubtitle != string.Empty))
            .ToListAsync();

        var paths = MediaSubtitleMatcher.ExtractGeneratedPaths(requests)
            .Select(MediaSubtitleMatcher.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            if (!string.IsNullOrWhiteSpace(request.TranslatedSubtitle))
            {
                paths.Add(MediaSubtitleMatcher.NormalizePath(request.TranslatedSubtitle));
            }
        }

        return paths;
    }

    private async Task<int> TryQueueSupplementalEmbeddedTranslationsAsync(
        IMedia media,
        MediaType mediaType,
        SubtitleSourceSelectionResult sourceSelection,
        IReadOnlyCollection<Subtitles> matchingExternalSubtitles,
        IReadOnlyCollection<string> targetLanguages,
        bool forceTranslation,
        bool forcePriority,
        int? maxTranslationsToQueue)
    {
        var supplementalEnabled = string.Equals(
            await _settingService.GetSetting(SettingKeys.Translation.TranslateSupplementalSubtitles),
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (!supplementalEnabled || sourceSelection.SupplementalCandidates.Count == 0)
        {
            return 0;
        }

        var queued = 0;
        var remainingQueueSlots = maxTranslationsToQueue ?? int.MaxValue;
        if (remainingQueueSlots <= 0)
        {
            return 0;
        }

        foreach (var assessment in sourceSelection.SupplementalCandidates)
        {
            if (remainingQueueSlots <= 0)
            {
                break;
            }

            var subtitle = assessment.Subtitle;
            if (string.IsNullOrWhiteSpace(assessment.MatchedLanguage))
            {
                continue;
            }

            var sourceType = SubtitleLanguageHelper.DetermineSubtitleType(subtitle);
            var outputCaption = SubtitleLanguageHelper.GetSupplementalOutputCaption(sourceType);
            if (string.IsNullOrWhiteSpace(outputCaption))
            {
                continue;
            }

            var requestedRequiredOutputFormats =
                await GetRequestedRequiredOutputFormatsAsync(subtitle.GetReadableSourceFormat());
            var requiredOutputFormats =
                SubtitleOutputModeHelper.DeserializeFormats(requestedRequiredOutputFormats);
            var existingSupplementalTargets = matchingExternalSubtitles
                .Where(subtitleFile => string.Equals(
                    subtitleFile.Caption,
                    outputCaption,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var languagesToTranslate = forceTranslation
                ? targetLanguages.ToList()
                : GetLanguagesMissingRequiredOutputFormats(
                        existingSupplementalTargets,
                        targetLanguages,
                        requiredOutputFormats,
                        allowSupplementalTargets: true)
                    .ToList();
            if (languagesToTranslate.Count == 0)
            {
                continue;
            }

            var sourceSnapshot = _sourceSubtitleSnapshotService.CreateEmbeddedSnapshot(
                subtitle,
                assessment.MatchedLanguage);
            foreach (var targetLanguage in languagesToTranslate.Take(remainingQueueSlots))
            {
                if (await HasActiveRequestAsync(
                        media.Id,
                        mediaType,
                        assessment.MatchedLanguage,
                        targetLanguage,
                        requestedRequiredOutputFormats,
                        sourceType,
                        sourceSnapshot.StreamIndex,
                        sourceSnapshot.Identity))
                {
                    continue;
                }

                await _translationRequestService.CreateRequest(new TranslateAbleSubtitle
                {
                    MediaId = media.Id,
                    MediaType = mediaType,
                    SubtitlePath = null,
                    TargetLanguage = targetLanguage,
                    SourceLanguage = assessment.MatchedLanguage,
                    SubtitleFormat = subtitle.GetReadableSourceFormat(),
                    SourceSubtitleType = sourceType,
                    SelectedStreamTitle = subtitle.Title,
                    IsForcedSubtitle = subtitle.IsForced,
                    SourceSnapshot = sourceSnapshot
                }, forcePriority);
                queued++;
                remainingQueueSlots--;
            }
        }

        return queued;
    }

    private static List<string> LimitQueuedLanguages(
        IEnumerable<string> languagesToTranslate,
        int? maxTranslationsToQueue)
    {
        if (!maxTranslationsToQueue.HasValue)
        {
            return languagesToTranslate.ToList();
        }

        return languagesToTranslate
            .Take(Math.Max(0, maxTranslationsToQueue.Value))
            .ToList();
    }

    private async Task<string> GetRequestedRequiredOutputFormatsAsync(string? sourceSubtitleFormat)
    {
        var subtitleOutputMode = await _settingService.GetSetting(SettingKeys.Translation.SubtitleOutputMode);
        return NormalizeRequiredOutputFormats(null, sourceSubtitleFormat, subtitleOutputMode);
    }

    private static HashSet<string> GetLanguagesMissingRequiredOutputFormats(
        IEnumerable<Subtitles> subtitles,
        IEnumerable<string> targetLanguages,
        IReadOnlyCollection<string> requiredOutputFormats,
        bool allowSupplementalTargets = false,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths = null)
    {
        var existingTargetFormats =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var subtitle in subtitles)
        {
            if (!allowSupplementalTargets &&
                ShouldSkipAsMainTarget(subtitle, knownGeneratedPrimaryTargetPaths))
            {
                continue;
            }

            var language = SubtitleLanguageHelper.NormalizeLanguageCode(subtitle.Language);
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            var format = SubtitleOutputModeHelper.NormalizeFormat(
                !string.IsNullOrWhiteSpace(subtitle.Format)
                    ? subtitle.Format
                    : !string.IsNullOrWhiteSpace(Path.GetExtension(subtitle.Path))
                        ? Path.GetExtension(subtitle.Path)
                        : Path.GetExtension(subtitle.FileName));
            if (string.IsNullOrWhiteSpace(format))
            {
                continue;
            }

            if (!existingTargetFormats.TryGetValue(language, out var formats))
            {
                formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                existingTargetFormats[language] = formats;
            }

            formats.Add(format);
        }

        return targetLanguages
            .Select(SubtitleLanguageHelper.NormalizeLanguageCode)
            .Where(targetLanguage => !string.IsNullOrWhiteSpace(targetLanguage))
            .Where(targetLanguage =>
                !existingTargetFormats.TryGetValue(targetLanguage, out var existingFormats) ||
                requiredOutputFormats.Any(requiredFormat => !existingFormats.Contains(requiredFormat)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Subtitles? SelectMainTargetSubtitle(
        IEnumerable<Subtitles> subtitles,
        string targetLanguage,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths = null)
    {
        return subtitles
            .Where(subtitle => !ShouldSkipAsMainTarget(subtitle, knownGeneratedPrimaryTargetPaths))
            .Where(subtitle => SubtitleLanguageHelper.LanguageMatches(subtitle.Language, targetLanguage))
            .OrderBy(subtitle => !string.IsNullOrWhiteSpace(subtitle.Caption))
            .ThenBy(subtitle => subtitle.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(subtitle => subtitle.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool ShouldSkipAsMainTarget(
        Subtitles subtitle,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths)
    {
        return ExternalSubtitleCandidateHelper.ShouldSkipAsMainTarget(subtitle) &&
               !IsKnownGeneratedPrimaryTarget(subtitle, knownGeneratedPrimaryTargetPaths);
    }

    private static bool IsKnownGeneratedPrimaryTarget(
        Subtitles subtitle,
        IReadOnlySet<string>? knownGeneratedPrimaryTargetPaths)
    {
        return !string.IsNullOrWhiteSpace(subtitle.Path) &&
               knownGeneratedPrimaryTargetPaths?.Contains(
                   MediaSubtitleMatcher.NormalizePath(subtitle.Path)) == true;
    }

    private static string NormalizeRequiredOutputFormats(
        string? requiredOutputFormats,
        string? sourceSubtitleFormat,
        string? subtitleOutputMode = null)
    {
        if (!string.IsNullOrWhiteSpace(requiredOutputFormats))
        {
            var normalized = SubtitleOutputModeHelper.SerializeFormats(
                SubtitleOutputModeHelper.DeserializeFormats(requiredOutputFormats));
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return SubtitleOutputModeHelper.SerializeFormats(
            SubtitleOutputModeHelper.GetRequiredOutputFormats(
                sourceSubtitleFormat,
                SubtitleOutputModeHelper.Parse(subtitleOutputMode)));
    }
}
