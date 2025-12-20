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
        { "ass", ".srt" },
        { "ssa", ".srt" },
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
    /// Tracks below this threshold are likely Signs/Songs or otherwise incomplete.
    /// </summary>
    public const int MinimumDialogueEntries = 100;

    public SubtitleExtractionService(
        ILogger<SubtitleExtractionService> logger,
        LingarrDbContext dbContext,
        ISettingService settingService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _settingService = settingService;
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

        // Determine output extension
        var extension = CodecToExtension.GetValueOrDefault(codecName, ".srt");
        var outputPath = GetExtractedSubtitlePath(outputDirectory, mediaFilePath, codecName, language, streamIndex);

        try
        {
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

            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting subtitle stream {StreamIndex} from {FilePath}",
                streamIndex, mediaFilePath);
            return null;
        }
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
            _logger.LogWarning("Could not find media file for episode: {FileName} in {Path}", episode.FileName, episode.Path);
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
            _logger.LogWarning("Could not find media file for movie: {FileName} in {Path}", movie.FileName, movie.Path);
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
        var outputFileName = $"{baseFileName}.{languageTag}{extension}";
        return Path.Combine(outputDirectory, outputFileName);
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
            return;
        }

        // Retry logic for concurrency conflicts (multiple jobs processing same media)
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Use ExecuteDeleteAsync for atomic deletion - won't fail if rows already deleted
                await _dbContext.EmbeddedSubtitles
                    .Where(e => e.EpisodeId == episodeId && e.MovieId == movieId)
                    .ExecuteDeleteAsync();

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
                    _dbContext.EmbeddedSubtitles.Add(sub);
                }

                await _dbContext.SaveChangesAsync();
                return; // Success, exit the retry loop
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

            if (items.Count == 0) return;

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

            if (filteredItems.Count == 0) return;

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
            
            var content = File.ReadAllText(filePath);
            // SRT: Count lines that are just numbers (entry markers)
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Count(line => int.TryParse(line.Trim(), out _) && line.Trim().All(char.IsDigit));
        }
        catch
        {
            return -1;
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
        List<string>? excludedPaths = null)
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
                mediaPath = Path.Combine(episode.Path, episode.FileName);
                outputDir = episode.Path;
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
                mediaPath = Path.Combine(movie.Path, movie.FileName);
                outputDir = movie.Path;
            }
            else
            {
                _logger.LogWarning("Unsupported media type for embedded extraction: {MediaType}", mediaType);
                return null;
            }

            // Get all candidates sorted by quality
            var candidates = GetSortedEmbeddedSubtitles(embeddedSubtitles, sourceLanguage);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("No suitable embedded subtitle found for source language: {Language}", sourceLanguage);
                return null;
            }

            // Iterate through candidates to find one that isn't excluded
            foreach (var candidate in candidates)
            {
                // Predict the output path to see if it should be excluded
                var predictedPath = GetExtractedSubtitlePath(
                    outputDir!,
                    mediaPath!,
                    candidate.CodecName,
                    candidate.Language,
                    candidate.StreamIndex);

                if (excludedPaths != null && excludedPaths.Contains(predictedPath))
                {
                    _logger.LogInformation(
                        "Skipping candidate Stream {StreamIndex} ({Language}) as its output path is excluded: {Path}",
                        candidate.StreamIndex, candidate.Language, predictedPath);
                    continue;
                }

                _logger.LogInformation(
                    "Attempting extraction of Stream {StreamIndex}, Language: {Language}, Codec: {Codec}",
                    candidate.StreamIndex, candidate.Language ?? "unknown", candidate.CodecName);

                try
                {
                    // Extract the subtitle
                    var extractedPath = await ExtractSubtitle(
                        mediaPath!,
                        candidate.StreamIndex,
                        outputDir!,
                        candidate.CodecName,
                        candidate.Language);

                    if (!string.IsNullOrEmpty(extractedPath))
                    {
                        // Update the database record
                        candidate.IsExtracted = true;
                        candidate.ExtractedPath = extractedPath;
                        await _dbContext.SaveChangesAsync();

                        return extractedPath;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract candidate Stream {StreamIndex}", candidate.StreamIndex);
                    // Continue to next candidate
                }
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

        // Only consider text-based subtitles
        var textBased = embeddedSubtitles.Where(s => s.IsTextBased).ToList();
        if (textBased.Count == 0)
        {
            return [];
        }

        // Prefer subtitles whose language matches the configured source language.
        // If none match, fall back to all text-based streams.
        var languageMatched = textBased
            .Where(s => SubtitleLanguageHelper.LanguageMatches(s.Language, sourceLanguage))
            .ToList();

        var candidates = languageMatched.Count > 0 ? languageMatched : textBased;

        // Score candidates and sort
        return candidates
            .Select(s => new { Subtitle = s, Score = SubtitleLanguageHelper.ScoreSubtitleCandidate(s, sourceLanguage) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Subtitle.StreamIndex) // Stability
            .Select(x => x.Subtitle)
            .ToList();
    }
}
