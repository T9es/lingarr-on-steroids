using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;

using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Service for detecting and extracting embedded subtitles from media files using FFmpeg/FFprobe.
/// </summary>
public class SubtitleExtractionService : ISubtitleExtractionService
{
    private readonly ILogger<SubtitleExtractionService> _logger;
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ISubtitleService _subtitleService;
    private readonly IEmbeddedSubtitleCacheService _embeddedSubtitleCacheService;
    private readonly ISubtitleLanguageDetectionService _languageDetectionService;

    // Codecs that are text-based and can be extracted/translated
    private static readonly HashSet<string> TextBasedCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass", "ssa", "srt", "subrip", "webvtt", "vtt", "mov_text", "text"
    };

    // Codecs that are image-based and cannot be translated without OCR
    private static readonly HashSet<string> ImageBasedCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "dvd_subtitle", "dvb_subtitle", "xsub", "pgssub"
    };

    // Map codec names to file extensions
    private static readonly Dictionary<string, string> CodecToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ass", ".ass" },
        { "ssa", ".ssa" },
        { "srt", ".srt" },
        { "subrip", ".srt" },
        { "webvtt", ".vtt" },
        { "vtt", ".vtt" },
        { "mov_text", ".srt" },
        { "text", ".srt" }
    };

    /// <summary>
    /// Comment marker added to extracted subtitle files to identify them as Lingarr-extracted.
    /// This allows distinguishing extracted files from user-provided external subtitles.
    /// </summary>
    public const string ExtractionMarkerPrefix = "; Lingarr-Extracted:";
    
    /// <summary>
    /// Minimum number of subtitle entries required for a track to be considered valid.
    /// Tracks below this threshold are likely Signs/Songs, Forced, or otherwise incomplete.
    /// Based on analysis: even "A Quiet Place" (minimal dialogue) has 165 entries.
    /// Signs/Forced tracks typically have 0-30 entries.
    /// </summary>
    public const int MinimumDialogueEntries = 50;

    public SubtitleExtractionService(
        ILogger<SubtitleExtractionService> logger,
        LingarrDbContext dbContext,
        ISettingService settingService,
        ISubtitleService subtitleService,
        IEmbeddedSubtitleCacheService embeddedSubtitleCacheService,
        ISubtitleLanguageDetectionService languageDetectionService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _settingService = settingService;
        _subtitleService = subtitleService;
        _embeddedSubtitleCacheService = embeddedSubtitleCacheService;
        _languageDetectionService = languageDetectionService;
    }

    /// <inheritdoc />
    public async Task<bool> IsFfmpegAvailable()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { "-version" }
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg/FFprobe is not available");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<EmbeddedSubtitle>> ProbeEmbeddedSubtitles(string mediaFilePath)
    {
        var result = new List<EmbeddedSubtitle>();

        if (!File.Exists(mediaFilePath))
        {
            _logger.LogWarning("Media file not found: {FilePath}", mediaFilePath);
            return result;
        }

        try
        {
            var json = await RunFfprobe(mediaFilePath);
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            var probeResult = JsonSerializer.Deserialize<FfprobeResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (probeResult?.Streams == null)
            {
                return result;
            }

            // Track subtitle stream index (separate from absolute stream index)
            var subtitleIndex = 0;
            foreach (var stream in probeResult.Streams)
            {
                if (stream.CodecType?.Equals("subtitle", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                var codecName = stream.CodecName?.ToLowerInvariant() ?? "unknown";
                var isTextBased = TextBasedCodecs.Contains(codecName);
                var isImageBased = ImageBasedCodecs.Contains(codecName);

                // Skip unknown codecs, assume image-based
                if (!isTextBased && !isImageBased)
                {
                    _logger.LogDebug("Unknown subtitle codec: {Codec}, treating as image-based", codecName);
                    isImageBased = true;
                }

                var embeddedSub = new EmbeddedSubtitle
                {
                    StreamIndex = subtitleIndex,
                    Language = stream.Tags?.Language,
                    Title = stream.Tags?.Title,
                    CodecName = codecName,
                    IsTextBased = isTextBased,
                    IsDefault = stream.Disposition?.Default == 1,
                    IsForced = stream.Disposition?.Forced == 1,
                    IsExtracted = false
                };

                result.Add(embeddedSub);
                subtitleIndex++;
            }

            _logger.LogInformation(
                "Probed {FilePath}: found {Total} subtitle streams ({TextBased} text-based, {ImageBased} image-based)",
                Path.GetFileName(mediaFilePath),
                result.Count,
                result.Count(s => s.IsTextBased),
                result.Count(s => !s.IsTextBased));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error probing media file: {FilePath}", mediaFilePath);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<string?> ExtractSubtitle(string mediaFilePath, int streamIndex, string outputDirectory, string codecName, string? language)
    {
        // Resolve the actual file path using the same logic as probing
        // (This handles cases where the DB path is missing the extension)
        var directory = Path.GetDirectoryName(mediaFilePath);
        var fileName = Path.GetFileName(mediaFilePath);
        
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            _logger.LogWarning("Invalid media file path for extraction: {FilePath}", mediaFilePath);
            return null;
        }

        var resolvedPath = FindMediaFile(directory, fileName);
        
        if (resolvedPath == null)
        {
             _logger.LogWarning("Media file not found for extraction: {FilePath}", mediaFilePath);
             return null;
        }
        
        // Use the actual file path on disk
        mediaFilePath = resolvedPath;

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var outputPath = GetExtractedSubtitlePath(outputDirectory, mediaFilePath, codecName, language, streamIndex);

        return await ExtractSubtitleToPathInternalAsync(mediaFilePath, streamIndex, outputPath, codecName);
    }

    /// <inheritdoc />
    public Task<string?> TryExtractEmbeddedSubtitleForRequestAsync(
        int mediaId,
        MediaType mediaType,
        string sourceLanguage,
        List<int>? excludedStreamIndices = null,
        int? preferredStreamIndex = null)
    {
        return TryExtractEmbeddedSubtitleInternalAsync(
            mediaId,
            mediaType,
            sourceLanguage,
            excludedStreamIndices,
            preferredStreamIndex,
            useInternalCache: true);
    }

    /// <inheritdoc />
    public async Task<string?> ExtractSubtitleToFile(
        string mediaFilePath,
        int streamIndex,
        string outputPath,
        string codecName)
    {
        try
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                _logger.LogWarning("Invalid subtitle extraction output path: {OutputPath}", outputPath);
                return null;
            }

            Directory.CreateDirectory(outputDirectory);

            var extension = CodecToExtension.GetValueOrDefault(codecName, ".srt");

            // ffmpeg -i input.mkv -map 0:s:{streamIndex} -c:s copy output.ass
            // If copying doesn't work for the target format, we remove -c:s copy for conversion
            var copyMode = extension is ".ass" or ".ssa";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(mediaFilePath);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add($"0:s:{streamIndex}");

            if (copyMode)
            {
                process.StartInfo.ArgumentList.Add("-c:s");
                process.StartInfo.ArgumentList.Add("copy");
            }

            process.StartInfo.ArgumentList.Add(outputPath);
            process.StartInfo.ArgumentList.Add("-y");

            _logger.LogDebug("Running FFmpeg: ffmpeg {Arguments}", string.Join(" ", process.StartInfo.ArgumentList));

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg extraction failed (exit code {ExitCode}): {Error}",
                    process.ExitCode, stderr);
                return null;
            }

            if (!File.Exists(outputPath))
            {
                _logger.LogWarning("Extraction completed but output file not found: {OutputPath}", outputPath);
                return null;
            }

            _logger.LogInformation("Extracted subtitle stream {StreamIndex} to: {OutputPath}",
                streamIndex, outputPath);

            // If it is an SRT file (text-based), perform cleanup (stripping ASS junk, deduping)
            // to ensure meaningful content for translation or viewing.
            if (extension == ".srt")
            {
                await CleanupSubtitleFile(outputPath);
            }
            else if (extension is ".ass" or ".ssa")
            {
                await EnsureExtractionMarkerAsync(outputPath);
            }

            if (_embeddedSubtitleCacheService.IsManagedCachePath(outputPath))
            {
                _embeddedSubtitleCacheService.RecordSourceSnapshot(outputPath, mediaFilePath);
            }

            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting subtitle stream {StreamIndex} from {FilePath} to {OutputPath}",
                streamIndex, mediaFilePath, outputPath);

            // Do NOT fold this into a null return: callers translate null into a generic
            // 'could not be resolved' failure, which hides the real cause (OOM, IO, parse
            // failure). Rethrow with a descriptive message so the failure stays visible
            // and diagnosable end to end. Transient failures still fail the request,
            // exactly as before — just with the actual reason surfaced.
            throw new InvalidOperationException(
                $"Subtitle extraction failed for stream {streamIndex} of {Path.GetFileName(mediaFilePath)}: {ex.Message}",
                ex);
        }
    }
    private async Task<string?> ExtractSubtitleToPathInternalAsync(
        string mediaFilePath,
        int streamIndex,
        string outputPath,
        string codecName)
    {
        return await ExtractSubtitleToFile(mediaFilePath, streamIndex, outputPath, codecName);
    }

    /// <inheritdoc />
    public async Task SyncEmbeddedSubtitles(Episode episode)
    {
        if (string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
        {
            _logger.LogDebug("Episode {EpisodeId} has no path/filename, skipping embedded subtitle sync", episode.Id);
            return;
        }

        var mediaPath = FindMediaFile(episode.Path, episode.FileName);
        if (mediaPath == null)
        {
            _logger.LogWarning(
                "Could not find media file for {Type}: {FileName} in {Path}. Directory exists: {DirExists}",
                "episode", episode.FileName, episode.Path, Directory.Exists(episode.Path));
            
            // Clear stale embedded subtitle records since the media file is no longer accessible
            await _dbContext.EmbeddedSubtitles
                .Where(e => e.EpisodeId == episode.Id && e.MovieId == null)
                .ExecuteDeleteAsync();
            DetachTrackedEmbeddedSubtitlesForMedia(_dbContext, episode.Id, null);
            _logger.LogInformation(
                "Cleared stale embedded subtitle records for episode {EpisodeId} - media file not found",
                episode.Id);
            
            return;
        }
        
        await SyncEmbeddedSubtitlesInternal(mediaPath, episode.Id, null);
    }

    /// <inheritdoc />
    public async Task SyncEmbeddedSubtitles(Movie movie)
    {
        if (string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
        {
            _logger.LogDebug("Movie {MovieId} has no path/filename, skipping embedded subtitle sync", movie.Id);
            return;
        }

        var mediaPath = FindMediaFile(movie.Path, movie.FileName);
        if (mediaPath == null)
        {
            _logger.LogWarning(
                "Could not find media file for {Type}: {FileName} in {Path}. Directory exists: {DirExists}",
                "movie", movie.FileName, movie.Path, Directory.Exists(movie.Path));
            
            // Clear stale embedded subtitle records since the media file is no longer accessible
            await _dbContext.EmbeddedSubtitles
                .Where(e => e.MovieId == movie.Id && e.EpisodeId == null)
                .ExecuteDeleteAsync();
            DetachTrackedEmbeddedSubtitlesForMedia(_dbContext, null, movie.Id);
            _logger.LogInformation(
                "Cleared stale embedded subtitle records for movie {MovieId} - media file not found",
                movie.Id);
            
            return;
        }
        
        await SyncEmbeddedSubtitlesInternal(mediaPath, null, movie.Id);
    }
    
    /// <summary>
    /// Generates the predicted output path for an extracted subtitle.
    /// </summary>
    private static string GetExtractedSubtitlePath(string outputDirectory, string mediaFilePath, string codecName, string? language, int streamIndex)
    {
        // Determine output extension
        var extension = CodecToExtension.GetValueOrDefault(codecName, ".srt");
        var baseFileName = Path.GetFileNameWithoutExtension(mediaFilePath);

        // Use language tag if available (e.g., ".eng.srt"), otherwise fall back to stream index
        var languageTag = !string.IsNullOrEmpty(language) ? language : $"stream{streamIndex}";
        var primaryPath = Path.Combine(outputDirectory, $"{baseFileName}.{languageTag}{extension}");

        // If the primary path exists and does NOT have our extraction marker, it's a user file.
        // We must not overwrite it. Fallback to a stream-specific name.
        if (File.Exists(primaryPath) && !IsLingarrExtracted(primaryPath))
        {
            var fallbackTag = !string.IsNullOrEmpty(language) ? $"{language}.s{streamIndex}" : $"stream{streamIndex}";
            return Path.Combine(outputDirectory, $"{baseFileName}.{fallbackTag}{extension}");
        }

        return primaryPath;
    }

    /// <summary>
    /// Finds the actual media file by searching for files that match the base filename.
    /// This is needed because FileName in the database may not include the extension.
    /// </summary>
    private string? FindMediaFile(string directory, string baseFileName)
    {
        _logger.LogDebug("FindMediaFile searching in: {Directory} for base: {BaseFileName}", directory, baseFileName);
        
        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("FindMediaFile: directory does not exist: {Directory}", directory);
            return null;
        }
        
        // Common video extensions to search for
        var videoExtensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".wmv" };
        
        // First try exact match with extension already in filename
        foreach (var ext in videoExtensions)
        {
            if (baseFileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                var exactPath = Path.Combine(directory, baseFileName);
                if (File.Exists(exactPath))
                {
                    _logger.LogDebug("FindMediaFile: found exact match: {Path}", exactPath);
                    return exactPath;
                }
            }
        }
        
        // Search for file with matching base name + video extension
        foreach (var ext in videoExtensions)
        {
            var path = Path.Combine(directory, baseFileName + ext);
            if (File.Exists(path))
            {
                _logger.LogDebug("FindMediaFile: found with extension: {Path}", path);
                return path;
            }
        }
        
        // Fallback: search directory for files starting with the base filename
        try
        {
            var files = Directory.GetFiles(directory);
            _logger.LogDebug("FindMediaFile: fallback search, {FileCount} files in directory", files.Length);
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                var ext = Path.GetExtension(file).ToLowerInvariant();
                
                // Check if it's a video file that matches our base filename
                if (videoExtensions.Contains(ext) && 
                    (fileNameWithoutExt == baseFileName || fileName.StartsWith(baseFileName + ".")))
                {
                    _logger.LogDebug("FindMediaFile: found via fallback search: {Path}", file);
                    return file;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for media file in directory: {Directory}", directory);
        }
        
        _logger.LogDebug("FindMediaFile: no matching file found for {BaseFileName} in {Directory}", baseFileName, directory);
        return null;
    }




    private async Task SyncEmbeddedSubtitlesInternal(string mediaPath, int? episodeId, int? movieId)
    {
        var embeddedSubs = await ProbeEmbeddedSubtitles(mediaPath);

        if (embeddedSubs.Count == 0)
        {
            var existingCount = await _dbContext.EmbeddedSubtitles
                .Where(e => e.EpisodeId == episodeId && e.MovieId == movieId)
                .ExecuteDeleteAsync();

            if (existingCount > 0)
            {
                _logger.LogInformation(
                    "Removed {Count} stale embedded subtitle records for media with no subtitle streams (EpisodeId={EpisodeId}, MovieId={MovieId})",
                    existingCount, episodeId, movieId);
            }

            return;
        }

        // Retry logic for concurrency conflicts (multiple jobs processing same media)
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Use ExecuteDeleteAsync for atomic deletion - won't fail if rows already deleted
                var existingSubtitles = await _dbContext.EmbeddedSubtitles
                    .AsNoTracking()
                    .Where(e => e.EpisodeId == episodeId && e.MovieId == movieId)
                    .ToListAsync();

                InvalidateManagedArtifactFiles(existingSubtitles, mediaPath);

                await _dbContext.EmbeddedSubtitles
                    .Where(e => e.EpisodeId == episodeId && e.MovieId == movieId)
                    .ExecuteDeleteAsync();
                DetachTrackedEmbeddedSubtitlesForMedia(_dbContext, episodeId, movieId);

                // Add new records
                foreach (var sub in embeddedSubs)
                {
                    // Reset entity state to prevent graph re-attachment issues during retries
                    // (e.g. preventing the Movie entity from being re-inserted if it satisfied a fixup previously)
                    sub.Id = 0; 
                    sub.Movie = null;
                    sub.Episode = null;

                    sub.EpisodeId = episodeId;
                    sub.MovieId = movieId;
                    CopyOcrMetadataIfSameStream(existingSubtitles, sub, mediaPath);
                    _dbContext.EmbeddedSubtitles.Add(sub);
                }

                await _dbContext.SaveChangesAsync();
                break; // Success, exit the retry loop
            }
            catch (DbUpdateException ex)
            {
                var isDuplicateEntry = false;
                var isDeadlock = false;
                
                // Check if the inner exception is a PostgreSQL duplicate entry error (23505) or Deadlock (40P01)
                if (ex.InnerException is PostgresException pgEx)
                {
                    if (pgEx.SqlState == "23505") // unique_violation
                    {
                        isDuplicateEntry = true;
                    }
                    else if (pgEx.SqlState == "40P01" || pgEx.SqlState == "40001") // deadlock_detected or serialization_failure
                    {
                        isDeadlock = true;
                    }
                }
                // Also check for standard concurrency exception
                else if (ex is DbUpdateConcurrencyException)
                {
                    isDuplicateEntry = true; // Treat concurrency conflict same as duplicate for retry purposes
                }

                // If deadlock occurs within an active transaction, we cannot retry locally as the transaction is aborted.
                // We must throw to let the ExecutionStrategy retry the entire transaction.
                if (isDeadlock && _dbContext.Database.CurrentTransaction != null)
                {
                    _logger.LogWarning(ex, "Deadlock detected in active transaction for EpisodeId={EpisodeId}, MovieId={MovieId}. Rethrowing to trigger transaction retry.", episodeId, movieId);
                    throw;
                }

                if (!isDuplicateEntry && !isDeadlock)
                {
                    // If it's not a concurrency/duplicate/deadlock issue, rethrow immediately
                    throw;
                }

                _logger.LogWarning(
                    "Concurrency/Deadlock conflict syncing embedded subtitles (attempt {Attempt}/{MaxRetries}) for EpisodeId={EpisodeId}, MovieId={MovieId}: {Message}",
                    attempt, maxRetries, episodeId, movieId, ex.Message);

                if (attempt == maxRetries)
                {
                    _logger.LogError(ex, 
                        "Failed to sync embedded subtitles after {MaxRetries} attempts for EpisodeId={EpisodeId}, MovieId={MovieId}",
                        maxRetries, episodeId, movieId);
                    throw;
                }

                // Clear the change tracker to remove stale entities before retry
                _dbContext.ChangeTracker.Clear();
                
                // Small delay before retry to reduce collision chance
                await Task.Delay(50 * attempt);
            }
        }

        // Run AI language detection for untagged streams after successful sync
        await TryDetectUnknownLanguagesAsync(episodeId, movieId);
    }

    private async Task TryDetectUnknownLanguagesAsync(int? episodeId, int? movieId)
    {
        var detectEnabled = string.Equals(
            await _settingService.GetSetting(SettingKeys.SubtitleExtraction.DetectUnknownLanguages),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!detectEnabled)
        {
            return;
        }

        try
        {
            var detected = await _languageDetectionService.DetectUnknownLanguagesAsync(
                movieId, episodeId);

            if (detected > 0)
            {
                _logger.LogInformation(
                    "Detected languages for {Count} untagged subtitle stream(s) via AI (EpisodeId={EpisodeId}, MovieId={MovieId})",
                    detected, episodeId, movieId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AI language detection failed during subtitle sync (EpisodeId={EpisodeId}, MovieId={MovieId}); streams will remain untagged",
                episodeId, movieId);
        }
    }

    internal static void DetachTrackedEmbeddedSubtitlesForMedia(
        LingarrDbContext dbContext,
        int? episodeId,
        int? movieId)
    {
        var trackedSubtitles = dbContext.ChangeTracker
            .Entries<EmbeddedSubtitle>()
            .Where(entry => entry.Entity.EpisodeId == episodeId && entry.Entity.MovieId == movieId)
            .ToList();

        foreach (var entry in trackedSubtitles)
        {
            entry.State = EntityState.Detached;
        }

        if (episodeId.HasValue)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<Episode>()
                         .Where(entry => entry.Entity.Id == episodeId.Value))
            {
                entry.Entity.EmbeddedSubtitles.Clear();
                entry.Collection(episode => episode.EmbeddedSubtitles).IsLoaded = false;
            }
        }

        if (movieId.HasValue)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<Movie>()
                         .Where(entry => entry.Entity.Id == movieId.Value))
            {
                entry.Entity.EmbeddedSubtitles.Clear();
                entry.Collection(movie => movie.EmbeddedSubtitles).IsLoaded = false;
            }
        }
    }

    private async Task CleanupSubtitleFile(string filePath)
    {
        try
        {
            // Parse the dirty SRT file
            var parser = new SrtParser();
            List<SubtitleItem> items;
            using (var stream = File.OpenRead(filePath))
            {
                items = parser.ParseStream(stream, System.Text.Encoding.UTF8);
            }

            if (items.Count == 0)
            {
                // Write the marker so Lingarr knows it extracted this file, even if it's empty
                await File.WriteAllTextAsync(filePath, $"{ExtractionMarkerPrefix} StreamIndex={0}, Entries=0\r\n\r\n");
                return;
            }

            // PRE-FILTER: Remove ASS drawing commands and empty lines
            var filteredItems = new List<SubtitleItem>();
            foreach (var item in items)
            {
                var cleanedLines = item.Lines
                    .Select(l => SubtitleFormatterService.RemoveMarkup(l))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                
                if (cleanedLines.Count == 0) continue;

                var combinedText = string.Join(" ", item.Lines); // Check original for drawings
                if (SubtitleFormatterService.IsAssDrawingCommand(combinedText)) continue;

                item.Lines = cleanedLines; // Update to cleaned lines
                item.PlaintextLines = cleanedLines; // Consistent
                filteredItems.Add(item);
            }

            if (filteredItems.Count == 0)
            {
                // Write the marker so Lingarr knows it extracted this file, even if it's empty after filtering
                await File.WriteAllTextAsync(filePath, $"{ExtractionMarkerPrefix} StreamIndex={0}, Entries=0\r\n\r\n");
                return;
            }

            // PASS 1: Merge Concurrent Layers (e.g. "Text Part 1" and "Text Part 2" appearing at same time)
            // Heuristic: If two subtitles start at roughly the same time (< 50ms diff) 
            // AND are both short duration (< 500ms, typical for animation frames), merge them.
            var layeredItems = new List<SubtitleItem>();
            if (filteredItems.Count > 0)
            {
                var current = filteredItems[0];
                for (int i = 1; i < filteredItems.Count; i++)
                {
                    var next = filteredItems[i];
                    var isConcurrent = Math.Abs(next.StartTime - current.StartTime) < 50; // 50ms tolerance
                    var isShort = (current.EndTime - current.StartTime) < 1000 && (next.EndTime - next.StartTime) < 1000;

                    if (isConcurrent && isShort)
                    {
                        // Merge next into current, but avoid duplicating identical text lines
                        foreach (var line in next.Lines)
                        {
                            if (!current.Lines.Contains(line))
                            {
                                current.Lines.Add(line);
                            }
                        }
                        current.EndTime = Math.Max(current.EndTime, next.EndTime);
                    }
                    else
                    {
                        layeredItems.Add(current);
                        current = next;
                    }
                }
                layeredItems.Add(current);
            }

            // PASS 2: Deduplicate Sequential Frames (Time Merging)
            // Heuristic: If two subtitles are identical AND timestamps are contiguous (or overlapping)
            // Gap tolerance: 100ms
            var finalItems = new List<SubtitleItem>();
            if (layeredItems.Count > 0)
            {
                var current = layeredItems[0];
                for (int i = 1; i < layeredItems.Count; i++)
                {
                    var next = layeredItems[i];
                    
                    var textA = string.Join("\n", current.Lines);
                    var textB = string.Join("\n", next.Lines);

                    // If text is identical AND timestamps are contiguous (or overlapping)
                    var gap = next.StartTime - current.EndTime;
                    if (textA == textB && gap < 100) 
                    {
                        // Merge time
                        current.EndTime = Math.Max(current.EndTime, next.EndTime);
                    }
                    else
                    {
                        finalItems.Add(current);
                        current = next;
                    }
                }
                finalItems.Add(current);
            }

            // Write back to file with extraction marker
            var sb = new System.Text.StringBuilder();
            
            // Add extraction marker comment at the top
            // SRT format allows comments starting with ; before the first entry
            sb.AppendLine($"{ExtractionMarkerPrefix} StreamIndex={0}, Entries={finalItems.Count}");
            sb.AppendLine();
            
            for (int i = 0; i < finalItems.Count; i++)
            {
                var item = finalItems[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine($"{FormatSingleTimeCode(item.StartTime)} --> {FormatSingleTimeCode(item.EndTime)}");
                foreach (var line in item.Lines)
                {
                    sb.AppendLine(line);
                }
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(filePath, sb.ToString());
            _logger.LogDebug("Cleaned extracted subtitle: {FilePath} (Original: {Org}, Final: {Final})", 
                filePath, items.Count, finalItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean extracted subtitle file: {FilePath}", filePath);
        }
    }

    private string FormatSingleTimeCode(int totalMs)
    {
        var ts = TimeSpan.FromMilliseconds(totalMs);
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }

    /// <summary>
    /// Checks if a subtitle file was extracted by Lingarr (has extraction marker).
    /// </summary>
    public static bool IsLingarrExtracted(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            
            // Read just the first line to check for marker
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            return firstLine?.StartsWith(ExtractionMarkerPrefix) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Counts the number of dialogue entries in a subtitle file (SRT format).
    /// Returns -1 if the file cannot be read.
    /// </summary>
    public static int CountSubtitleEntries(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return -1;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".ass" or ".ssa" => File.ReadLines(filePath)
                    .Count(line => line.TrimStart().StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)),
                ".srt" => CountTextCueEntries(filePath, isWebVtt: false),
                ".vtt" => CountTextCueEntries(filePath, isWebVtt: true),
                _ => File.ReadLines(filePath)
                    .Count(line =>
                    {
                        var trimmed = line.Trim();
                        return int.TryParse(trimmed, out _) && trimmed.All(char.IsDigit);
                    })
            };
        }
        catch
        {
            return -1;
        }
    }

    private static int CountTextCueEntries(string filePath, bool isWebVtt)
    {
        var count = 0;
        var block = new List<string>();

        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (IsTextCueBlock(block, isWebVtt))
                {
                    count++;
                }

                block.Clear();
                continue;
            }

            block.Add(line.Trim());
        }

        if (IsTextCueBlock(block, isWebVtt))
        {
            count++;
        }

        return count;
    }

    private static bool IsTextCueBlock(IReadOnlyList<string> block, bool isWebVtt)
    {
        if (block.Count == 0)
        {
            return false;
        }

        var timeCodeIndex = -1;
        for (var index = 0; index < block.Count; index++)
        {
            var line = block[index];
            if (isWebVtt && ShouldSkipWebVttHeaderLine(line))
            {
                continue;
            }

            if (isWebVtt && IsWebVttMetadataBlock(line))
            {
                return false;
            }

            if (line.Contains("-->", StringComparison.Ordinal))
            {
                timeCodeIndex = index;
                break;
            }
        }

        if (timeCodeIndex < 0)
        {
            return false;
        }

        return block
            .Skip(timeCodeIndex + 1)
            .Any(line => !string.IsNullOrWhiteSpace(SubtitleFormatterService.RemoveMarkup(line)));
    }

    private static bool ShouldSkipWebVttHeaderLine(string line)
    {
        return line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("X-TIMESTAMP-MAP=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebVttMetadataBlock(string line)
    {
        return line.StartsWith("NOTE", StringComparison.Ordinal) ||
               line.StartsWith("STYLE", StringComparison.Ordinal) ||
               line.StartsWith("REGION", StringComparison.Ordinal);
    }

    internal static async Task EnsureExtractionMarkerAsync(string filePath)
    {
        if (!File.Exists(filePath) || IsLingarrExtracted(filePath))
        {
            return;
        }

        // Never buffer the whole file: a large ASS (observed 223 MB, ~450 MB as UTF-16) blew
        // the container heap when 4 workers ran marker writes concurrently. The entry count
        // streams via File.ReadLines and the content pass below uses a fixed-size buffer,
        // so peak memory stays constant regardless of file size.
        var entryCount = CountSubtitleEntries(filePath);
        if (entryCount < 0)
        {
            throw new IOException($"Cannot read subtitle file to write extraction marker: {filePath}");
        }

        // Write marker header + content into a temp file in one streaming pass, then atomically
        // move it over the original so readers never observe a partially marked file.
        var tempPath = $"{filePath}.lingarr-tmp";
        try
        {
            var hasContent = false;
            await using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(source))
            await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(destination, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync($"{ExtractionMarkerPrefix} StreamIndex=0, Entries={entryCount}\n\n");

                var buffer = new char[8192];
                int charsRead;
                while ((charsRead = await reader.ReadAsync(buffer)) > 0)
                {
                    if (!hasContent)
                    {
                        for (var i = 0; i < charsRead; i++)
                        {
                            if (!char.IsWhiteSpace(buffer[i]))
                            {
                                hasContent = true;
                                break;
                            }
                        }
                    }

                    await writer.WriteAsync(buffer.AsMemory(0, charsRead));
                }
            }

            if (!hasContent)
            {
                // Matches the previous behavior: empty/whitespace-only files stay unmarked.
                return;
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Checks if a subtitle file is sparse (below minimum entries threshold).
    /// </summary>
    public static bool IsSparseSubtitle(string filePath)
    {
        var count = CountSubtitleEntries(filePath);
        return count >= 0 && count < MinimumDialogueEntries;
    }

    private async Task<string?> RunFfprobe(string mediaFilePath)
    {
        try
        {
            _logger.LogDebug("Running FFprobe on: {FullPath}", mediaFilePath);
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("-v");
            process.StartInfo.ArgumentList.Add("quiet");
            process.StartInfo.ArgumentList.Add("-print_format");
            process.StartInfo.ArgumentList.Add("json");
            process.StartInfo.ArgumentList.Add("-show_streams");
            process.StartInfo.ArgumentList.Add("-select_streams");
            process.StartInfo.ArgumentList.Add("s");
            process.StartInfo.ArgumentList.Add(mediaFilePath);

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogDebug("FFprobe result for {FileName}: exit={ExitCode}, output={Length} chars, stderr={StdErrLength} chars",
                Path.GetFileName(mediaFilePath), process.ExitCode, output.Length, stderr.Length);
            
            if (!string.IsNullOrEmpty(stderr))
            {
                _logger.LogDebug("FFprobe stderr: {StdErr}", stderr);
            }
            
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFprobe exited with code {ExitCode} for {FilePath}. Stderr: {StdErr}",
                    process.ExitCode, mediaFilePath, stderr);
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running FFprobe on {FilePath}", mediaFilePath);
            return null;
        }
    }


    // FFprobe JSON result models
    private class FfprobeResult
    {
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }
    }

    private class FfprobeStream
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        
        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }
        
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }
        
        [JsonPropertyName("disposition")]
        public FfprobeDisposition? Disposition { get; set; }
        
        [JsonPropertyName("tags")]
        public FfprobeTags? Tags { get; set; }
    }

    private class FfprobeDisposition
    {
        [JsonPropertyName("default")]
        public int Default { get; set; }
        
        [JsonPropertyName("forced")]
        public int Forced { get; set; }
    }

    private class FfprobeTags
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    /// <inheritdoc />
    public async Task<string?> TryExtractEmbeddedSubtitle(
        int mediaId, 
        MediaType mediaType, 
        string sourceLanguage, 
        List<int>? excludedStreamIndices = null,
        int? preferredStreamIndex = null)
    {
        return await TryExtractEmbeddedSubtitleInternalAsync(
            mediaId,
            mediaType,
            sourceLanguage,
            excludedStreamIndices,
            preferredStreamIndex,
            useInternalCache: false);
    }

    private async Task<string?> TryExtractEmbeddedSubtitleInternalAsync(
        int mediaId,
        MediaType mediaType,
        string sourceLanguage,
        List<int>? excludedStreamIndices,
        int? preferredStreamIndex,
        bool useInternalCache)
    {
        try
        {
            List<EmbeddedSubtitle>? embeddedSubtitles = null;
            string? mediaPath = null;
            string? outputDir = null;

            // Find the media and its embedded subtitles based on MediaType
            if (mediaType == MediaType.Episode)
            {
                var episode = await _dbContext.Episodes
                    .Include(e => e.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(e => e.Id == mediaId);

                if (episode == null)
                {
                    _logger.LogWarning("Episode not found: {MediaId}", mediaId);
                    return null;
                }

                if (string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
                {
                    _logger.LogWarning("Episode has no path/filename: {MediaId}", mediaId);
                    return null;
                }

                // Sync embedded subtitles if not already done
                if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
                {
                    await SyncEmbeddedSubtitles(episode);
                    await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
                }

                embeddedSubtitles = episode.EmbeddedSubtitles;
                mediaPath = FindMediaFile(episode.Path, episode.FileName);
                if (mediaPath == null)
                {
                    _logger.LogWarning(
                        "Could not find media file for episode: {FileName} in {Path}. Directory exists: {DirExists}",
                        episode.FileName,
                        episode.Path,
                        Directory.Exists(episode.Path));
                    return null;
                }
                outputDir = useInternalCache
                    ? _embeddedSubtitleCacheService.CacheRootPath
                    : episode.Path;
            }
            else if (mediaType == MediaType.Movie)
            {
                var movie = await _dbContext.Movies
                    .Include(m => m.EmbeddedSubtitles)
                    .FirstOrDefaultAsync(m => m.Id == mediaId);

                if (movie == null)
                {
                    _logger.LogWarning("Movie not found: {MediaId}", mediaId);
                    return null;
                }

                if (string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
                {
                    _logger.LogWarning("Movie has no path/filename: {MediaId}", mediaId);
                    return null;
                }

                // Sync embedded subtitles if not already done
                if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
                {
                    await SyncEmbeddedSubtitles(movie);
                    await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
                }

                embeddedSubtitles = movie.EmbeddedSubtitles;
                mediaPath = FindMediaFile(movie.Path, movie.FileName);
                if (mediaPath == null)
                {
                    _logger.LogWarning(
                        "Could not find media file for movie: {FileName} in {Path}. Directory exists: {DirExists}",
                        movie.FileName,
                        movie.Path,
                        Directory.Exists(movie.Path));
                    return null;
                }
                outputDir = useInternalCache
                    ? _embeddedSubtitleCacheService.CacheRootPath
                    : movie.Path;
            }
            else
            {
                _logger.LogWarning("Unsupported media type for embedded extraction: {MediaType}", mediaType);
                return null;
            }

            if (embeddedSubtitles != null &&
                InvalidateStaleManagedArtifacts(embeddedSubtitles, mediaPath!))
            {
                await _dbContext.SaveChangesAsync();
            }

            if (useInternalCache)
            {
                _embeddedSubtitleCacheService.EnsureCacheDirectory();
            }

            // Tracks the last exception thrown while extracting a candidate. If every
            // candidate ends in an exception, that is a real failure (e.g. OOM during
            // marker write) and must not surface as the generic "no suitable subtitle".
            Exception? lastExtractionFailure = null;

            // If a preferred stream index is specified, try that first
            if (preferredStreamIndex.HasValue)
            {
                var preferredSubtitle = embeddedSubtitles?.FirstOrDefault(s => 
                    s.StreamIndex == preferredStreamIndex.Value && s.IsReadableSource());

                if (preferredSubtitle != null)
                {
                    if (preferredSubtitle.HasUsableOcr())
                    {
                        _embeddedSubtitleCacheService.Touch(preferredSubtitle.OcrExtractedPath!);
                        return preferredSubtitle.OcrExtractedPath;
                    }

                    _logger.LogInformation(
                        "Using preferred stream index {StreamIndex} for extraction",
                        preferredStreamIndex.Value);

                    try
                    {
                        var extractedPath = useInternalCache
                            ? await ExtractCandidateToInternalCacheAsync(
                                mediaId,
                                mediaType,
                                mediaPath!,
                                preferredSubtitle)
                            : await ExtractSubtitle(
                                mediaPath!,
                                preferredSubtitle.StreamIndex,
                                outputDir!,
                                preferredSubtitle.CodecName,
                                preferredSubtitle.Language);

                        if (!string.IsNullOrEmpty(extractedPath))
                        {
                            // Update the database record
                            preferredSubtitle.IsExtracted = true;
                            preferredSubtitle.ExtractedPath = extractedPath;
                            await _dbContext.SaveChangesAsync();

                            return extractedPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastExtractionFailure = ex;
                        _logger.LogWarning(ex, 
                            "Failed to extract preferred stream {StreamIndex}, falling back to auto-selection", 
                            preferredStreamIndex.Value);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Preferred stream index {StreamIndex} not found or not readable, falling back to auto-selection",
                        preferredStreamIndex.Value);
                }
            }

            // Get all candidates sorted by quality (fallback behavior)
            var candidates = GetSortedEmbeddedSubtitles(embeddedSubtitles, sourceLanguage);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("No suitable embedded subtitle found for source language: {Language}", sourceLanguage);
                return null;
            }

            var viableCandidates = new List<ExtractedSubtitleCandidate>();

            var extractionOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract readable candidates first, then use content analysis as a second-stage tie-break.
            foreach (var candidate in candidates)
            {
                // Skip streams that have already been tried
                if (excludedStreamIndices != null && excludedStreamIndices.Contains(candidate.StreamIndex))
                {
                    _logger.LogDebug(
                        "Skipping candidate Stream {StreamIndex} ({Language}) as it was already tried",
                        candidate.StreamIndex, candidate.Language);
                    continue;
                }

                _logger.LogInformation(
                    "Attempting extraction of Stream {StreamIndex}, Language: {Language}, Codec: {Codec}",
                    candidate.StreamIndex, candidate.Language ?? "unknown", candidate.CodecName);

                try
                {
                    if (candidate.HasUsableOcr())
                    {
                        var ocrEntryCount = candidate.OcrCueCount ?? CountSubtitleEntries(candidate.OcrExtractedPath!);
                        viableCandidates.Add(
                            new ExtractedSubtitleCandidate(
                                candidate,
                                candidate.OcrExtractedPath!,
                                ocrEntryCount,
                                SubtitleLanguageHelper.ScoreSubtitleCandidate(candidate, sourceLanguage),
                                null,
                                true));
                        continue;
                    }

                    var extractionLanguageTag = candidate.Language;
                    var candidateOutputPath = useInternalCache
                        ? _embeddedSubtitleCacheService.GetCachePath(
                            mediaId,
                            mediaType,
                            candidate.StreamIndex,
                            candidate.CodecName,
                            candidate.Language)
                        : GetExtractedSubtitlePath(
                            outputDir!,
                            mediaPath!,
                            candidate.CodecName,
                            extractionLanguageTag,
                            candidate.StreamIndex);
                    if (!useInternalCache && !extractionOutputPaths.Add(candidateOutputPath))
                    {
                        extractionLanguageTag = BuildStreamSpecificLanguageTag(
                            candidate.Language,
                            candidate.StreamIndex);
                        candidateOutputPath = GetExtractedSubtitlePath(
                            outputDir!,
                            mediaPath!,
                            candidate.CodecName,
                            extractionLanguageTag,
                            candidate.StreamIndex);
                        extractionOutputPaths.Add(candidateOutputPath);
                    }

                    var existedBeforeExtraction = File.Exists(candidateOutputPath);

                    // Extract the subtitle
                    var extractedPath = useInternalCache
                        ? await ExtractCandidateToInternalCacheAsync(
                            mediaId,
                            mediaType,
                            mediaPath!,
                            candidate)
                        : await ExtractSubtitle(
                            mediaPath!,
                            candidate.StreamIndex,
                            outputDir!,
                            candidate.CodecName,
                            extractionLanguageTag);

                    if (!string.IsNullOrEmpty(extractedPath))
                    {
                        // Validate entry count - sparse tracks (Signs/Songs/Forced) have very few entries
                        // Check Lingarr marker FIRST to avoid wasting I/O on non-Lingarr files
                        if (!IsLingarrExtracted(extractedPath))
                        {
                            _logger.LogWarning(
                                "Stream {StreamIndex} extracted file has no Lingarr marker, preserving (may be user file)",
                                candidate.StreamIndex);
                            // Don't delete user files, but don't use them either
                            excludedStreamIndices ??= new List<int>();
                            excludedStreamIndices.Add(candidate.StreamIndex);
                            continue;
                        }
                        
                        var entryCount = CountSubtitleEntries(extractedPath);
                        
                        if (entryCount < MinimumDialogueEntries)
                        {
                            _logger.LogWarning(
                                "Stream {StreamIndex} has only {Entries} entries (minimum: {Min}), likely sparse track. Deleting and trying next candidate.",
                                candidate.StreamIndex, entryCount, MinimumDialogueEntries);
                            
                            // Delete the sparse file immediately - we don't want residue
                            try
                            {
                                File.Delete(extractedPath);
                            }
                            catch (Exception deleteEx)
                            {
                                _logger.LogWarning(deleteEx, "Failed to delete sparse subtitle file: {Path}", extractedPath);
                            }
                            
                            // Mark this stream as tried so we don't attempt it again
                            excludedStreamIndices ??= new List<int>();
                            excludedStreamIndices.Add(candidate.StreamIndex);
                            
                            continue; // Try next candidate
                        }
                        
                        var analysis = await AnalyzeExtractedCandidateAsync(candidate, extractedPath);
                        var score = SubtitleLanguageHelper.ScoreSubtitleCandidate(
                            candidate,
                            sourceLanguage,
                            analysis?.ContentScoreAdjustment ?? 0);

                        viableCandidates.Add(
                            new ExtractedSubtitleCandidate(
                                candidate,
                                extractedPath,
                                entryCount,
                                score,
                                analysis,
                                existedBeforeExtraction));

                        _logger.LogInformation(
                            "Successfully extracted Stream {StreamIndex} with {Entries} entries to: {Path}. Content score={Score}, pathological={Pathological}",
                            candidate.StreamIndex,
                            entryCount,
                            extractedPath,
                            score,
                            analysis?.IsPathological ?? false);
                    }
                }
                catch (Exception ex)
                {
                    lastExtractionFailure = ex;
                    _logger.LogWarning(ex, "Failed to extract candidate Stream {StreamIndex}", candidate.StreamIndex);
                    // Continue to next candidate
                }
            }

            if (viableCandidates.Count > 0)
            {
                var selectedCandidate = viableCandidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Subtitle.StreamIndex)
                    .First();
                foreach (var discardedCandidate in viableCandidates.Where(candidate => !ReferenceEquals(candidate, selectedCandidate)))
                {
                    DeleteDiscardedExtraction(discardedCandidate);
                }

                if (!selectedCandidate.Subtitle.HasUsableOcr())
                {
                    selectedCandidate.Subtitle.IsExtracted = true;
                    selectedCandidate.Subtitle.ExtractedPath = selectedCandidate.ExtractedPath;
                }
                await _dbContext.SaveChangesAsync();

                if (selectedCandidate.Analysis?.IsPathological == true)
                {
                    _logger.LogWarning(
                        "Selected embedded Stream {StreamIndex}, but its ASS content still looks pathological: drawingEvents={DrawingEvents}, duplicateRatio={DuplicateRatio:F2}, avgProviderChars={AverageChars:F2}. Translation batching/dedupe will protect provider calls.",
                        selectedCandidate.Subtitle.StreamIndex,
                        selectedCandidate.Analysis.DrawingEvents,
                        selectedCandidate.Analysis.DuplicateRatio,
                        selectedCandidate.Analysis.AverageProviderCharsPerTranslatableCue);
                }

                _logger.LogInformation(
                    "Selected extracted embedded Stream {StreamIndex} with content-aware score {Score} and {Entries} entries: {Path}",
                    selectedCandidate.Subtitle.StreamIndex,
                    selectedCandidate.Score,
                    selectedCandidate.EntryCount,
                    selectedCandidate.ExtractedPath);

                return selectedCandidate.ExtractedPath;
            }
            
            if (lastExtractionFailure != null)
            {
                // Every candidate failed with an exception (OOM, IO, parse, ...): surface the
                // real cause instead of returning null, which would degrade into the generic
                // 'Source subtitle could not be resolved' failure with the root cause lost.
                _logger.LogError(lastExtractionFailure,
                    "All embedded subtitle candidates failed extraction for media {MediaId}", mediaId);
                throw new InvalidOperationException(
                    $"Embedded subtitle extraction failed for all candidates of media {mediaId}: {lastExtractionFailure.Message}",
                    lastExtractionFailure);
            }

            _logger.LogWarning("All suitable embedded subtitle candidates failed extraction or were excluded");
            return null;
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw extraction failures
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during embedded subtitle extraction for media {MediaId}", mediaId);
            throw new InvalidOperationException($"Embedded subtitle extraction failed: {ex.Message}", ex);
        }
    }

    private Task<string?> ExtractCandidateToInternalCacheAsync(
        int mediaId,
        MediaType mediaType,
        string mediaPath,
        EmbeddedSubtitle candidate)
    {
        var cachePath = _embeddedSubtitleCacheService.GetCachePath(
            mediaId,
            mediaType,
            candidate.StreamIndex,
            candidate.CodecName,
            candidate.Language);

        return ExtractSubtitleToPathInternalAsync(
            mediaPath,
            candidate.StreamIndex,
            cachePath,
            candidate.CodecName);
    }

    /// <inheritdoc />
    public async Task ClearExtractionMetadataAsync(int mediaId, MediaType mediaType, string extractedPath)
    {
        if (string.IsNullOrWhiteSpace(extractedPath))
        {
            return;
        }

        var subtitle = await _dbContext.EmbeddedSubtitles.FirstOrDefaultAsync(es =>
            es.ExtractedPath == extractedPath &&
            ((mediaType == MediaType.Movie && es.MovieId == mediaId) ||
             (mediaType == MediaType.Episode && es.EpisodeId == mediaId)));

        if (subtitle == null)
        {
            return;
        }

        if (!subtitle.IsExtracted && string.IsNullOrEmpty(subtitle.ExtractedPath))
        {
            return;
        }

        subtitle.IsExtracted = false;
        subtitle.ExtractedPath = null;
        await _dbContext.SaveChangesAsync();
    }

    private async Task<AssSubtitleSourceAnalysis?> AnalyzeExtractedCandidateAsync(
        EmbeddedSubtitle candidate,
        string extractedPath)
    {
        var previousExtractedPath = candidate.ExtractedPath;
        candidate.ExtractedPath = extractedPath;

        try
        {
            return await AssSubtitleSourceAnalyzer.AnalyzeExtractedSubtitleAsync(
                candidate,
                _subtitleService);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to analyze extracted subtitle stream {StreamIndex} at {ExtractedPath}. Falling back to metadata score.",
                candidate.StreamIndex,
                extractedPath);
            return null;
        }
        finally
        {
            candidate.ExtractedPath = previousExtractedPath;
        }
    }

    private void DeleteDiscardedExtraction(ExtractedSubtitleCandidate candidate)
    {
        if (candidate.ExistedBeforeExtraction)
        {
            return;
        }

        try
        {
            File.Delete(candidate.ExtractedPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to delete discarded extracted subtitle candidate at {ExtractedPath}",
                candidate.ExtractedPath);
        }
    }

    /// <summary>
    /// Returns a list of embedded subtitle candidates sorted by suitability for translation.
    /// Prioritizes: text-based > matching source language > full/dialogue tracks > defaults.
    /// </summary>
    private static List<EmbeddedSubtitle> GetSortedEmbeddedSubtitles(List<EmbeddedSubtitle>? embeddedSubtitles, string sourceLanguage)
    {
        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
        {
            return [];
        }

        var readableSubtitles = embeddedSubtitles.Where(s => s.IsReadableSource()).ToList();
        if (readableSubtitles.Count == 0)
        {
            return [];
        }

        // Prefer subtitles whose language matches the configured source language.
        // If none match, fall back to all readable streams.
        var languageMatched = readableSubtitles
            .Where(s => SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage))
            .ToList();

        var candidates = languageMatched.Count > 0 ? languageMatched : readableSubtitles;

        // Score candidates and sort
        return candidates
            .Select(s => new { Subtitle = s, Score = SubtitleLanguageHelper.ScoreSubtitleCandidate(s, sourceLanguage) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Subtitle.StreamIndex) // Stability
            .Select(x => x.Subtitle)
            .ToList();
    }

    private void CopyOcrMetadataIfSameStream(
        IReadOnlyCollection<EmbeddedSubtitle> existingSubtitles,
        EmbeddedSubtitle newSubtitle,
        string mediaPath)
    {
        var existing = existingSubtitles.FirstOrDefault(subtitle =>
            subtitle.StreamIndex == newSubtitle.StreamIndex &&
            string.Equals(subtitle.CodecName, newSubtitle.CodecName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(subtitle.Language, newSubtitle.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(subtitle.Title, newSubtitle.Title, StringComparison.OrdinalIgnoreCase) &&
            subtitle.IsDefault == newSubtitle.IsDefault &&
            subtitle.IsForced == newSubtitle.IsForced);
        if (existing == null)
        {
            return;
        }

        if (SubtitleOcrStatePolicy.IsStaleTransient(existing, DateTime.UtcNow))
        {
            SubtitleOcrStatePolicy.ResetStaleTransient(newSubtitle);
            return;
        }

        if (IsStaleManagedArtifact(existing.OcrExtractedPath, mediaPath))
        {
            _embeddedSubtitleCacheService.Invalidate(existing.OcrExtractedPath!);
            SubtitleOcrStatePolicy.Reset(newSubtitle);
            return;
        }

        newSubtitle.OcrStatus = existing.OcrStatus;
        newSubtitle.OcrExtractedPath = existing.OcrExtractedPath;
        newSubtitle.OcrError = existing.OcrError;
        newSubtitle.OcrAttemptedAt = existing.OcrAttemptedAt;
        newSubtitle.OcrCompletedAt = existing.OcrCompletedAt;
        newSubtitle.OcrCueCount = existing.OcrCueCount;
        newSubtitle.OcrQualityScore = existing.OcrQualityScore;
        newSubtitle.OcrIssueSummary = existing.OcrIssueSummary;
        newSubtitle.OcrApprovedAt = existing.OcrApprovedAt;
    }

    private bool InvalidateStaleManagedArtifacts(
        IEnumerable<EmbeddedSubtitle> subtitles,
        string mediaPath)
    {
        var changed = false;
        foreach (var subtitle in subtitles)
        {
            if (IsStaleManagedArtifact(subtitle.ExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.ExtractedPath!);
                subtitle.IsExtracted = false;
                subtitle.ExtractedPath = null;
                changed = true;
            }

            if (IsStaleManagedArtifact(subtitle.OcrExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.OcrExtractedPath!);
                SubtitleOcrStatePolicy.Reset(subtitle);
                changed = true;
            }
        }

        return changed;
    }

    private void InvalidateManagedArtifactFiles(
        IEnumerable<EmbeddedSubtitle> subtitles,
        string mediaPath)
    {
        foreach (var subtitle in subtitles)
        {
            if (IsStaleManagedArtifact(subtitle.ExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.ExtractedPath!);
            }

            if (IsStaleManagedArtifact(subtitle.OcrExtractedPath, mediaPath))
            {
                _embeddedSubtitleCacheService.Invalidate(subtitle.OcrExtractedPath!);
            }
        }
    }

    private bool IsStaleManagedArtifact(string? path, string mediaPath)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               _embeddedSubtitleCacheService.IsManagedCachePath(path) &&
               !_embeddedSubtitleCacheService.IsCurrentForSource(path, mediaPath);
    }

    private static string? BuildStreamSpecificLanguageTag(string? language, int streamIndex)
    {
        return string.IsNullOrWhiteSpace(language)
            ? $"stream{streamIndex}"
            : $"{language}.s{streamIndex}";
    }

    private sealed record ExtractedSubtitleCandidate(
        EmbeddedSubtitle Subtitle,
        string ExtractedPath,
        int EntryCount,
        int Score,
        AssSubtitleSourceAnalysis? Analysis,
        bool ExistedBeforeExtraction);

    /// <inheritdoc />
    public async Task<List<AvailableSubtitleResponse>> ListAvailableSubtitlesAsync(int mediaId, MediaType mediaType)
    {
        var result = new List<AvailableSubtitleResponse>();
        List<EmbeddedSubtitle>? embeddedSubtitles = null;
        string? mediaPath = null;

        // Get media and its embedded subtitles
        if (mediaType == MediaType.Episode)
        {
            var episode = await _dbContext.Episodes
                .Include(e => e.EmbeddedSubtitles)
                .FirstOrDefaultAsync(e => e.Id == mediaId);

            if (episode == null)
            {
                _logger.LogWarning("Episode not found: {MediaId}", mediaId);
                return result;
            }

            if (string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
            {
                _logger.LogWarning("Episode has no path/filename: {MediaId}", mediaId);
                return result;
            }

            mediaPath = FindMediaFile(episode.Path, episode.FileName);

            // Sync embedded subtitles if not already done
            if (episode.EmbeddedSubtitles == null || episode.EmbeddedSubtitles.Count == 0)
            {
                await SyncEmbeddedSubtitles(episode);
                await _dbContext.Entry(episode).Collection(e => e.EmbeddedSubtitles).LoadAsync();
            }

            embeddedSubtitles = episode.EmbeddedSubtitles;
        }
        else if (mediaType == MediaType.Movie)
        {
            var movie = await _dbContext.Movies
                .Include(m => m.EmbeddedSubtitles)
                .FirstOrDefaultAsync(m => m.Id == mediaId);

            if (movie == null)
            {
                _logger.LogWarning("Movie not found: {MediaId}", mediaId);
                return result;
            }

            if (string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
            {
                _logger.LogWarning("Movie has no path/filename: {MediaId}", mediaId);
                return result;
            }

            mediaPath = FindMediaFile(movie.Path, movie.FileName);

            // Sync embedded subtitles if not already done
            if (movie.EmbeddedSubtitles == null || movie.EmbeddedSubtitles.Count == 0)
            {
                await SyncEmbeddedSubtitles(movie);
                await _dbContext.Entry(movie).Collection(m => m.EmbeddedSubtitles).LoadAsync();
            }

            embeddedSubtitles = movie.EmbeddedSubtitles;
        }
        else
        {
            _logger.LogWarning("Unsupported media type for listing subtitles: {MediaType}", mediaType);
            return result;
        }

        if (embeddedSubtitles == null || embeddedSubtitles.Count == 0)
        {
            return result;
        }

        var metadataChanged = mediaPath != null &&
                              InvalidateStaleManagedArtifacts(embeddedSubtitles, mediaPath);

        var staleSubtitles = embeddedSubtitles
            .Where(sub => sub.IsExtracted &&
                          !string.IsNullOrEmpty(sub.ExtractedPath) &&
                          !File.Exists(sub.ExtractedPath))
            .ToList();

        if (staleSubtitles.Count > 0)
        {
            foreach (var staleSubtitle in staleSubtitles)
            {
                staleSubtitle.IsExtracted = false;
                staleSubtitle.ExtractedPath = null;
            }
            metadataChanged = true;
        }

        if (metadataChanged)
        {
            await _dbContext.SaveChangesAsync();
        }

        // Build response with entry counts for extracted subtitles
        foreach (var sub in embeddedSubtitles)
        {
            int? entryCount = null;
            bool? isSparse = null;

            if (sub.IsExtracted && !string.IsNullOrEmpty(sub.ExtractedPath) && File.Exists(sub.ExtractedPath))
            {
                entryCount = CountSubtitleEntries(sub.ExtractedPath);
                isSparse = entryCount >= 0 && entryCount < MinimumDialogueEntries;
            }
            else if (sub.HasUsableOcr())
            {
                entryCount = sub.OcrCueCount ?? CountSubtitleEntries(sub.OcrExtractedPath!);
                isSparse = entryCount >= 0 && entryCount < MinimumDialogueEntries;
            }

            result.Add(new AvailableSubtitleResponse
            {
                Id = sub.Id,
                StreamIndex = sub.StreamIndex,
                Language = sub.Language,
                Title = sub.Title,
                CodecName = sub.CodecName,
                IsTextBased = sub.IsTextBased,
                IsDefault = sub.IsDefault,
                IsForced = sub.IsForced,
                IsExtracted = sub.IsExtracted,
                ExtractedPath = sub.ExtractedPath,
                EntryCount = entryCount,
                IsSparse = isSparse,
                OcrStatus = sub.OcrStatus,
                OcrExtractedPath = sub.OcrExtractedPath,
                OcrError = sub.OcrError,
                OcrAttemptedAt = sub.OcrAttemptedAt,
                OcrCompletedAt = sub.OcrCompletedAt,
                OcrCueCount = sub.OcrCueCount,
                OcrQualityScore = sub.OcrQualityScore,
                OcrIssueSummary = sub.OcrIssueSummary,
                OcrApprovedAt = sub.OcrApprovedAt,
                IsOcrSupported = !sub.IsTextBased &&
                                 (string.Equals(sub.CodecName, "hdmv_pgs_subtitle", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(sub.CodecName, "pgssub", StringComparison.OrdinalIgnoreCase)),
                IsOcrUsable = sub.HasUsableOcr()
            });
        }

        // Sort: text-based first, then by stream index
        return result
            .OrderByDescending(s => s.IsTextBased)
            .ThenBy(s => s.StreamIndex)
            .ToList();
    }
}
