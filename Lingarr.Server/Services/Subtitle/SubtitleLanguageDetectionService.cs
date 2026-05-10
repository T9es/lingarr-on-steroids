using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Subtitle;

public class SubtitleLanguageDetectionService : ISubtitleLanguageDetectionService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ILogger<SubtitleLanguageDetectionService> _logger;

    private const int MinimumEntriesForDetection = 10;
    private const int SampleDurationSeconds = 30;
    private const int SampleLineCount = 5;

    public SubtitleLanguageDetectionService(
        LingarrDbContext dbContext,
        ISettingService settingService,
        ITranslationServiceFactory translationServiceFactory,
        ILogger<SubtitleLanguageDetectionService> logger)
    {
        _dbContext = dbContext;
        _settingService = settingService;
        _translationServiceFactory = translationServiceFactory;
        _logger = logger;
    }

    public async Task<int> DetectUnknownLanguagesAsync(
        int? movieId = null,
        int? episodeId = null,
        CancellationToken ct = default)
    {
        var untaggedStreams = await QueryUntaggedStreamsAsync(movieId, episodeId);
        if (untaggedStreams.Count == 0)
        {
            return 0;
        }

        var detectableStreams = await FilterDetectableStreamsAsync(untaggedStreams, ct);
        if (detectableStreams.Count == 0)
        {
            return 0;
        }

        var maxBatchSize = await GetMaxBatchSizeAsync();
        var totalUpdated = 0;

        foreach (var batch in Chunk(detectableStreams, maxBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var samples = await CollectSamplesAsync(batch, ct);
            if (samples.Count == 0)
            {
                continue;
            }

            var detected = await DetectBatchAsync(samples, batch, ct);
            totalUpdated += detected;
        }

        return totalUpdated;
    }

    private async Task<List<EmbeddedSubtitle>> QueryUntaggedStreamsAsync(int? movieId, int? episodeId)
    {
        IQueryable<EmbeddedSubtitle> query = _dbContext.EmbeddedSubtitles
            .Where(s => s.IsTextBased)
            .Where(s => s.Language == null || s.Language == "" || s.Language == "und");

        if (movieId.HasValue)
        {
            query = query.Where(s => s.MovieId == movieId.Value);
        }
        else if (episodeId.HasValue)
        {
            query = query.Where(s => s.EpisodeId == episodeId.Value);
        }

        return await query.ToListAsync();
    }

    private async Task<List<(EmbeddedSubtitle Subtitle, string MediaPath)>> FilterDetectableStreamsAsync(
        List<EmbeddedSubtitle> streams, CancellationToken ct)
    {
        var result = new List<(EmbeddedSubtitle Subtitle, string MediaPath)>(streams.Count);

        foreach (var stream in streams)
        {
            var mediaPath = await ResolveMediaPathAsync(stream);
            if (mediaPath == null)
            {
                _logger.LogDebug(
                    "Skipping untagged stream {StreamIndex}: media file not found for MovieId={MovieId}, EpisodeId={EpisodeId}",
                    stream.StreamIndex, stream.MovieId, stream.EpisodeId);
                continue;
            }

            if (!File.Exists(mediaPath))
            {
                _logger.LogDebug(
                    "Skipping untagged stream {StreamIndex}: media file does not exist at {Path}",
                    stream.StreamIndex, mediaPath);
                continue;
            }

            result.Add((stream, mediaPath));
        }

        return result;
    }

    private async Task<string?> ResolveMediaPathAsync(EmbeddedSubtitle subtitle)
    {
        if (subtitle.MovieId.HasValue)
        {
            var movie = await _dbContext.Movies.FindAsync(subtitle.MovieId.Value);
            if (movie == null || string.IsNullOrEmpty(movie.Path) || string.IsNullOrEmpty(movie.FileName))
            {
                return null;
            }

            return Path.Combine(movie.Path, movie.FileName);
        }

        if (subtitle.EpisodeId.HasValue)
        {
            var episode = await _dbContext.Episodes.FindAsync(subtitle.EpisodeId.Value);
            if (episode == null || string.IsNullOrEmpty(episode.Path) || string.IsNullOrEmpty(episode.FileName))
            {
                return null;
            }

            return Path.Combine(episode.Path, episode.FileName);
        }

        return null;
    }

    private async Task<int> GetMaxBatchSizeAsync()
    {
        var batchSetting = await _settingService.GetSetting(SettingKeys.Translation.MaxBatchSize);
        return int.TryParse(batchSetting, out var size) && size > 0 ? size : 120;
    }

    private async Task<Dictionary<int, string>> CollectSamplesAsync(
        List<(EmbeddedSubtitle Subtitle, string MediaPath)> batch,
        CancellationToken ct)
    {
        var samples = new Dictionary<int, string>();

        foreach (var (subtitle, mediaPath) in batch)
        {
            ct.ThrowIfCancellationRequested();

            var sample = await ExtractSampleLinesAsync(mediaPath, subtitle.StreamIndex, ct);
            if (sample == null || sample.Count < MinimumEntriesForDetection)
            {
                _logger.LogDebug(
                    "Skipping stream {StreamIndex}: not enough subtitle entries ({Count})",
                    subtitle.StreamIndex, sample?.Count ?? 0);
                continue;
            }

            var lines = sample.Take(SampleLineCount).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var mediaTitle = await GetMediaTitleAsync(subtitle);
            var builder = new StringBuilder();
            builder.AppendLine($"Stream {subtitle.StreamIndex} (from \"{mediaTitle}\"):");
            for (var i = 0; i < lines.Count; i++)
            {
                builder.AppendLine($"  {i + 1}: {lines[i]}");
            }

            samples[subtitle.StreamIndex] = builder.ToString();
        }

        return samples;
    }

    private async Task<List<string>?> ExtractSampleLinesAsync(
        string mediaPath, int streamIndex, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{mediaPath}\" -map 0:s:{streamIndex} -f srt -t {SampleDurationSeconds} -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParseSrtLines(output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to extract sample from stream {StreamIndex} in {Path}",
                streamIndex, Path.GetFileName(mediaPath));
            return null;
        }
    }

    private static List<string> ParseSrtLines(string srtContent)
    {
        var lines = new List<string>();
        var blocks = srtContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var blockLines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (blockLines.Length < 3)
            {
                continue;
            }

            for (var i = 2; i < blockLines.Length; i++)
            {
                var trimmed = blockLines[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    lines.Add(trimmed);
                }
            }
        }

        return lines;
    }

    private async Task<string> GetMediaTitleAsync(EmbeddedSubtitle subtitle)
    {
        if (subtitle.MovieId.HasValue)
        {
            var movie = await _dbContext.Movies.FindAsync(subtitle.MovieId.Value);
            return movie?.Title ?? "Unknown Movie";
        }

        if (subtitle.EpisodeId.HasValue)
        {
            var episode = await _dbContext.Episodes.FindAsync(subtitle.EpisodeId.Value);
            return episode?.Title ?? "Unknown Episode";
        }

        return "Unknown Media";
    }

    private async Task<int> DetectBatchAsync(
        Dictionary<int, string> samples,
        List<(EmbeddedSubtitle Subtitle, string MediaPath)> batch,
        CancellationToken ct)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        var serviceType = await _settingService.GetSetting(SettingKeys.Translation.ServiceType);
        if (string.IsNullOrEmpty(serviceType))
        {
            _logger.LogWarning("No translation service configured; skipping language detection");
            return 0;
        }

        ITranslationService translationService;
        try
        {
            translationService = _translationServiceFactory.CreateTranslationService(serviceType);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex,
                "Cannot create translation service '{ServiceType}' for language detection; skipping",
                serviceType);
            return 0;
        }

        var prompt = BuildDetectionPrompt(samples);

        string response;
        try
        {
            response = await translationService.TranslateAsync(
                prompt, "en", "en", null, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI language detection request failed; streams will remain untagged");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("AI language detection returned empty response; streams will remain untagged");
            return 0;
        }

        var detections = ParseDetectionResponse(response);

        var updatedCount = 0;
        foreach (var (streamIndex, languageCode) in detections)
        {
            var match = batch.Find(b => b.Subtitle.StreamIndex == streamIndex);
            if (match.Subtitle == null)
            {
                continue;
            }

            if (!SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(languageCode, out var normalized))
            {
                _logger.LogWarning(
                    "AI detected unknown language code '{Code}' for stream {StreamIndex}; skipping update",
                    languageCode, streamIndex);
                continue;
            }

            match.Subtitle.Language = normalized;
            updatedCount++;

            _logger.LogInformation(
                "Detected language '{Language}' for stream {StreamIndex} (MovieId={MovieId}, EpisodeId={EpisodeId})",
                normalized, streamIndex, match.Subtitle.MovieId, match.Subtitle.EpisodeId);
        }

        if (updatedCount > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return updatedCount;
    }

    private static string BuildDetectionPrompt(Dictionary<int, string> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Detect the language of each subtitle sample below. " +
                       "Respond with ONLY a JSON array of objects with \"stream_index\" (integer) and " +
                       "\"language_code\" (ISO 639-2 two-letter code, e.g. \"en\", \"ja\", \"de\"). " +
                       "Use \"und\" if the language cannot be determined.");
        sb.AppendLine();
        sb.AppendLine("Do not include any explanation, markdown formatting, or code fences. " +
                       "Return ONLY the raw JSON array.");
        sb.AppendLine();

        foreach (var (streamIndex, sample) in samples)
        {
            sb.AppendLine(sample);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static Dictionary<int, string> ParseDetectionResponse(string response)
    {
        var result = new Dictionary<int, string>();

        var json = response.Trim();
        var fenceStart = json.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = json.IndexOf('\n', fenceStart);
            if (afterFence < 0)
            {
                afterFence = fenceStart + 3;
            }

            var fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd > afterFence)
            {
                json = json.Substring(afterFence + 1, fenceEnd - afterFence - 1).Trim();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (!element.TryGetProperty("stream_index", out var indexProp) ||
                        !element.TryGetProperty("language_code", out var codeProp))
                    {
                        continue;
                    }

                    var streamIndex = indexProp.GetInt32();
                    var languageCode = codeProp.GetString() ?? "und";

                    result[streamIndex] = languageCode;
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("detections", out var detections) &&
                    detections.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in detections.EnumerateArray())
                    {
                        if (!element.TryGetProperty("stream_index", out var indexProp) ||
                            !element.TryGetProperty("language_code", out var codeProp))
                        {
                            continue;
                        }

                        var streamIndex = indexProp.GetInt32();
                        var languageCode = codeProp.GetString() ?? "und";

                        result[streamIndex] = languageCode;
                    }
                }
                else
                {
                    foreach (var property in root.EnumerateObject())
                    {
                        if (int.TryParse(property.Name, out var streamIndex) &&
                            property.Value.ValueKind == JsonValueKind.String)
                        {
                            result[streamIndex] = property.Value.GetString() ?? "und";
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                {
                    var inner = trimmed.Trim('{', '}');
                    var parts = inner.Split(',');
                    int? streamIndex = null;
                    string? langCode = null;

                    foreach (var part in parts)
                    {
                        var kv = part.Split(':', 2);
                        if (kv.Length != 2)
                        {
                            continue;
                        }

                        var key = kv[0].Trim().Trim('"');
                        var value = kv[1].Trim().Trim('"', ',', ' ');

                        if (key == "stream_index" && int.TryParse(value, out var idx))
                        {
                            streamIndex = idx;
                        }
                        else if (key == "language_code")
                        {
                            langCode = value;
                        }
                    }

                    if (streamIndex.HasValue && langCode != null)
                    {
                        result[streamIndex.Value] = langCode;
                    }
                }
            }
        }

        return result;
    }

    private static List<List<T>> Chunk<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>(source.Count / chunkSize + 1);
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }

        return chunks;
    }
}