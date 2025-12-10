using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Service for detecting and extracting embedded subtitles from media files using FFmpeg/FFprobe.
/// </summary>
public class SubtitleExtractionService : ISubtitleExtractionService
{
    private readonly ILogger<SubtitleExtractionService> _logger;
    private readonly LingarrDbContext _dbContext;

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

    public SubtitleExtractionService(
        ILogger<SubtitleExtractionService> logger,
        LingarrDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
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
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
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
        var baseFileName = Path.GetFileNameWithoutExtension(mediaFilePath);
        
        // Use language tag if available (e.g., ".eng.srt"), otherwise fall back to stream index
        var languageTag = !string.IsNullOrEmpty(language) ? language : $"stream{streamIndex}";
        var outputFileName = $"{baseFileName}.{languageTag}{extension}";
        var outputPath = Path.Combine(outputDirectory, outputFileName);

        try
        {
            // ffmpeg -i input.mkv -map 0:s:{streamIndex} -c:s copy output.ass
            // If copying doesn't work for the target format, we remove -c:s copy for conversion
            var copyMode = extension is ".ass" or ".ssa";
            var copyArgs = copyMode ? "-c:s copy" : "";

            var arguments = $"-i \"{mediaFilePath}\" -map 0:s:{streamIndex} {copyArgs} \"{outputPath}\" -y";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogDebug("Running FFmpeg: ffmpeg {Arguments}", arguments);

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

        // Remove existing embedded subtitle records for this media
        var existing = await _dbContext.EmbeddedSubtitles
            .Where(e => e.EpisodeId == episodeId && e.MovieId == movieId)
            .ToListAsync();

        if (existing.Any())
        {
            _dbContext.EmbeddedSubtitles.RemoveRange(existing);
        }

        // Add new records
        foreach (var sub in embeddedSubs)
        {
            sub.EpisodeId = episodeId;
            sub.MovieId = movieId;
            _dbContext.EmbeddedSubtitles.Add(sub);
        }

        await _dbContext.SaveChangesAsync();
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
                        // Merge next into current
                        current.Lines.AddRange(next.Lines);
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
                    // Gap tolerance: 100ms
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

            // Write back to file
            var sb = new System.Text.StringBuilder();
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

    private async Task<string?> RunFfprobe(string mediaFilePath)
    {
        try
        {
            var arguments = $"-v quiet -print_format json -show_streams -select_streams s \"{mediaFilePath}\"";
            _logger.LogDebug("Running FFprobe on: {FullPath}", mediaFilePath);
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

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
}
