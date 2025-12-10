using System.Diagnostics;
using System.Text.Json;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services.Subtitle;
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
        { "ass", ".ass" },
        { "ssa", ".ssa" },
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
        if (!File.Exists(mediaFilePath))
        {
            _logger.LogWarning("Media file not found for extraction: {FilePath}", mediaFilePath);
            return null;
        }

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

        var mediaPath = Path.Combine(episode.Path, episode.FileName);
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

        var mediaPath = Path.Combine(movie.Path, movie.FileName);
        await SyncEmbeddedSubtitlesInternal(mediaPath, null, movie.Id);
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
            var lines = await File.ReadAllLinesAsync(filePath);
            var output = new List<string>();
            var subtitleBuffer = new List<string>();
            
            // For deduplication
            var lastKeptText = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (subtitleBuffer.Count >= 3)
                    {
                        // Buffer structure: [0]=Number, [1]=Timecode, [2...]=Text
                        // We skip the first 2 metadata lines to evaluate the text
                        var textLines = subtitleBuffer.Skip(2).ToList();
                        var combinedText = string.Join(" ", textLines);
                        
                        // Use the formatter to strip markup (<font>, {\an5} etc)
                        // This serves two purposes:
                        // 1. To check for "junk" (ASS drawing commands, single-letter karaoke)
                        // 2. To write CLEAN text to the file (removes visual clutter)
                        var cleanedTextLines = textLines.Select(SubtitleFormatterService.RemoveMarkup).ToList();
                        var cleanedCombinedText = string.Join(" ", cleanedTextLines);

                        if (!SubtitleFormatterService.IsAssDrawingCommand(combinedText)) // Check original for context? No, IsAss checks cleaned inside.
                        {
                            // DEDUPLICATION:
                            // If text is effectively identical to the last kept line, likely an ASS layer duplicate -> Skip
                            if (cleanedCombinedText != lastKeptText)
                            {
                                output.Add(subtitleBuffer[0]); // Number
                                output.Add(subtitleBuffer[1]); // Timecode
                                
                                // Write the CLEANED text lines
                                output.AddRange(cleanedTextLines);
                                output.Add(""); // Blank line separator
                                
                                lastKeptText = cleanedCombinedText;
                            }
                        }
                    }
                    subtitleBuffer.Clear();
                }
                else
                {
                    subtitleBuffer.Add(line);
                }
            }

            // Process remaining buffer
            if (subtitleBuffer.Count >= 3)
            {
                var textLines = subtitleBuffer.Skip(2).ToList();
                var cleanedTextLines = textLines.Select(SubtitleFormatterService.RemoveMarkup).ToList();
                var cleanedCombinedText = string.Join(" ", cleanedTextLines);

                if (!SubtitleFormatterService.IsAssDrawingCommand(string.Join(" ", textLines)))
                {
                    if (cleanedCombinedText != lastKeptText)
                    {
                        output.Add(subtitleBuffer[0]);
                        output.Add(subtitleBuffer[1]);
                        output.AddRange(cleanedTextLines);
                        output.Add("");
                    }
                }
            }
            
            // Overwrite the file with cleaned content
            await File.WriteAllLinesAsync(filePath, output);
            _logger.LogDebug("Cleaned extracted subtitle: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean extracted subtitle file: {FilePath}", filePath);
        }
    }

    private async Task<string?> RunFfprobe(string mediaFilePath)
    {
        try
        {
            var arguments = $"-v quiet -print_format json -show_streams -select_streams s \"{mediaFilePath}\"";
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
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFprobe exited with code {ExitCode} for {FilePath}",
                    process.ExitCode, mediaFilePath);
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
        public List<FfprobeStream>? Streams { get; set; }
    }

    private class FfprobeStream
    {
        public int Index { get; set; }
        public string? CodecName { get; set; }
        public string? CodecType { get; set; }
        public FfprobeDisposition? Disposition { get; set; }
        public FfprobeTags? Tags { get; set; }
    }

    private class FfprobeDisposition
    {
        public int Default { get; set; }
        public int Forced { get; set; }
    }

    private class FfprobeTags
    {
        public string? Language { get; set; }
        public string? Title { get; set; }
    }
}
