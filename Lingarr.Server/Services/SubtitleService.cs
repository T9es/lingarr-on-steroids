using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using SubtitleValidationOptions = Lingarr.Server.Models.SubtitleValidationOptions;

namespace Lingarr.Server.Services;

public class SubtitleService : ISubtitleService
{
    private static readonly string[] SupportedExtensions = [".srt", ".ssa", ".ass", ".vtt"];
    private static readonly string[] SupportedCaptions = ["sdh", "cc", "forced", "hi"];
    private static readonly char[] WhitespaceCharacters = [' ', '\t', '\n', '\r'];
    private const int MaxFilenameBytes = 255;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".wmv", ".flv", ".ts", ".m2ts", ".mov", ".webm", ".mpg", ".mpeg", ".m4v"
    };

    private readonly ILogger<SubtitleService> _logger;

    public SubtitleService(
        ILogger<SubtitleService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<List<Subtitles>> GetAllSubtitles(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogInformation(
                "Failed to collect subtitles in path |Red|{Path}|/Red|. Try reindexing or verify that the media is correctly set up in the source system.",
                path);
            return Task.FromResult(new List<Subtitles>());
        }

        var subtitles = new List<Subtitles>();
        // Optimize: Scan directory once for all files, then filter in memory
        var allFiles = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(file);
            var parts = fileName.Split('.').Reverse().ToList();
            var language = "";
            var caption = "";

            // First look for caption
            var captionPart = parts.FirstOrDefault(p => SupportedCaptions.Contains(p.ToLower()));
            if (captionPart != null)
            {
                caption = captionPart.ToLower();
                parts.Remove(captionPart);
            }

            // Then look for language in remaining parts
            var languagePart = parts.FirstOrDefault(p => TryGetLanguageByPart(p, out var code));
            if (languagePart != null && TryGetLanguageByPart(languagePart, out var languageCode))
            {
                language = languageCode;
                parts.Remove(languagePart);
            }
            // Hindi is an exception, if we didn't find a language, and we did found Hindi, We set that as language
            else if (caption == "hi" && language == "")
            {
                language = caption;
                caption = "";
            }

            subtitles.Add(new Subtitles
            {
                Path = file,
                FileName = fileName,
                Language = language ?? "unknown",
                Caption = caption,
                Format = extension
            });
        }

        return Task.FromResult(subtitles);
    }

    /// <inheritdoc />
    public async Task<List<SubtitleItem>> ReadSubtitles(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        ISubtitleParser parser = extension switch
        {
            ".srt" => new SrtParser(),
            ".ssa" or ".ass" => new SsaParser(),
            ".vtt" => new VttParser(),
            _ => throw new NotSupportedException($"Subtitle format {extension} is not supported")
        };

        await using var fileStream = File.OpenRead(filePath);
        return parser.ParseStream(fileStream, Encoding.UTF8);
    }

    /// <inheritdoc />
    public async Task WriteSubtitles(string filePath, List<SubtitleItem> subtitles, bool stripSubtitleFormatting)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        ISubtitleWriter writer = extension switch
        {
            ".srt" => new SrtWriter(),
            ".ssa" or ".ass" => new SsaWriter(),
            ".vtt" => new VttWriter(),
            _ => throw new NotSupportedException($"Subtitle format {extension} is not supported")
        };

        await using var fileStream = File.Create(filePath);
        await writer.WriteStreamAsync(fileStream, subtitles, stripSubtitleFormatting);
    }

    /// <inheritdoc />
    public string CreateFilePath(string originalPath, string targetLanguage, string subtitleTag, string? outputFormat = null)
    {
        return CreateFilePathInternal(originalPath, targetLanguage, subtitleTag, outputFormat);
    }

    /// <inheritdoc />
    public IEnumerable<string> CreateFallbackPaths(
        string originalPath,
        string targetLanguage,
        string subtitleTag,
        string subtitleTagShort,
        string? outputFormat = null,
        string? forcedCaption = null)
    {
        var paths = new List<string>();

        // 1. Full Tag
        paths.Add(CreateFilePathInternal(originalPath, targetLanguage, subtitleTag, outputFormat, forcedCaption));

        // 2. Short Tag (if provided and different)
        if (!string.IsNullOrEmpty(subtitleTagShort) &&
            !string.Equals(subtitleTagShort, subtitleTag, StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(CreateFilePathInternal(originalPath, targetLanguage, subtitleTagShort, outputFormat, forcedCaption));
        }

        // 3. No Tag (if different from previous)
        var noTagPath = CreateFilePathInternal(originalPath, targetLanguage, null, outputFormat, forcedCaption);
        if (!paths.Contains(noTagPath))
        {
            paths.Add(noTagPath);
        }

        // 4. Truncated (last resort): byte-safe variant that always fits the
        // filesystem per-component limit, added only when even the shortest
        // named variant exceeds it.
        var truncatedPath = CreateTruncatedFilePath(noTagPath);
        if (!string.IsNullOrEmpty(truncatedPath) && !paths.Contains(truncatedPath))
        {
            paths.Add(truncatedPath);
        }

        return paths.Distinct();
    }

    /// <summary>
    /// Produces a byte-safe truncated variant of the supplied path that fits the
    /// filesystem per-component limit (255 bytes), used as a last-resort fallback
    /// when even the shortest named variant exceeds it. Returns null when the
    /// supplied path already fits and no truncation is needed.
    /// </summary>
    private static string? CreateTruncatedFilePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (Encoding.UTF8.GetByteCount(fileName) <= MaxFilenameBytes)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var hash = ComputeStableShortHash(baseName);
        var suffix = $"-{hash}{extension}";
        var maxBaseBytes = Math.Max(8, MaxFilenameBytes - Encoding.UTF8.GetByteCount(suffix));
        var truncatedBase = TruncateUtf8(baseName, maxBaseBytes);
        return Path.Combine(directory, truncatedBase + suffix);
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder();
        var byteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (byteCount + runeBytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            byteCount += runeBytes;
        }

        return builder.ToString();
    }

    private static string ComputeStableShortHash(string value)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hashBytes.AsSpan(0, 4)).ToLowerInvariant();
    }

    private string CreateFilePathInternal(
        string originalPath,
        string targetLanguage,
        string? subtitleTag,
        string? outputFormat = null,
        string? forcedCaption = null)
    {
        var extension = !string.IsNullOrWhiteSpace(outputFormat)
            ? SubtitleOutputModeHelper.NormalizeFormat(outputFormat)
            : Path.GetExtension(originalPath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var directory = Path.GetDirectoryName(originalPath) ?? string.Empty;

        if (IsVideoFilePath(originalPath))
        {
            var newFileName = BuildVideoDerivedSubtitleName(
                fileNameWithoutExtension, targetLanguage, forcedCaption, subtitleTag, extension);
            return Path.Combine(directory, newFileName);
        }

        var parts = fileNameWithoutExtension.Split('.');
        var reversedParts = parts.Reverse().ToList();

        // Extract caption if present
        string? caption = null;
        int captionIndex = reversedParts.FindIndex(p => SupportedCaptions.Contains(p.ToLower()));
        if (captionIndex != -1)
        {
            caption = reversedParts[captionIndex].ToLowerInvariant();
            reversedParts.RemoveAt(captionIndex);
        }

        if (!string.IsNullOrWhiteSpace(forcedCaption))
        {
            caption = forcedCaption.ToLowerInvariant();
        }

        // Extract language
        int languageIndex = reversedParts.FindIndex(part => TryGetLanguageByPart(part, out _));
        if (languageIndex != -1)
        {
            reversedParts.RemoveAt(languageIndex);
        }

        // Resolve target language code
        string? targetLanguageCode = null;
        if (!string.IsNullOrEmpty(targetLanguage)
            && !TryGetLanguageByPart(targetLanguage, out targetLanguageCode))
        {
            targetLanguageCode = targetLanguage;
        }

        // Reconstruct base parts
        var baseParts = reversedParts.AsEnumerable().Reverse().ToList();
        var newParts = new List<string>(baseParts);
        
        if (targetLanguageCode != null)
        {
            newParts.Add(targetLanguageCode.ToLowerInvariant());
        }

        // Add caption if present
        if (!string.IsNullOrEmpty(caption))
        {
            newParts.Add(caption);
        }
        
        // Add tag if provided
        if (!string.IsNullOrEmpty(subtitleTag))
        {
            newParts.Add(subtitleTag.ToLowerInvariant());
        }
        
        // Build subtitle file name and path
        var result = string.Join(".", newParts) + extension;
        return Path.Combine(directory, result);
    }

    private static bool IsVideoFilePath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    private static string BuildVideoDerivedSubtitleName(
        string videoBaseName,
        string targetLanguage,
        string? forcedCaption,
        string? subtitleTag,
        string extension)
    {
        var parts = new List<string> { videoBaseName };

        if (!string.IsNullOrEmpty(targetLanguage))
        {
            if (TryGetLanguageByPart(targetLanguage, out var code))
            {
                parts.Add(code.ToLowerInvariant());
            }
            else
            {
                parts.Add(targetLanguage.ToLowerInvariant());
            }
        }

        if (!string.IsNullOrWhiteSpace(forcedCaption))
        {
            parts.Add(forcedCaption.ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(subtitleTag))
        {
            parts.Add(subtitleTag.ToLowerInvariant());
        }

        return string.Join(".", parts) + extension;
    }

    /// <inheritdoc />
    public List<SubtitleItem> FixOverlappingSubtitles(List<SubtitleItem> subtitles)
    {
        const int buffer = 20;
        const int baseMinDuration = 1500;
        const int maxDuration = 6000;
        const double wordsPerSecond = 2.5;
        var fixCount = 0;

        for (var index = 1; index < subtitles.Count - 1; index++)
        {
            var prev = subtitles[index - 1];
            var current = subtitles[index];
            var next = subtitles[index + 1];

            var wordCount = CountWords(current.Lines);
            var optimalDuration = CalculateOptimalDuration(wordCount, wordsPerSecond, baseMinDuration, maxDuration);

            var hasOverlap = current.EndTime + buffer > next.StartTime;
            var needsAdjustment = hasOverlap;
            if (!needsAdjustment)
            {
                continue;
            }

            var currentDuration = current.EndTime - current.StartTime;

            var overlapTime = Math.Max(0, current.EndTime + buffer - next.StartTime);
            var optimalTimeNeeded = Math.Max(0, optimalDuration - currentDuration);
            var timeNeeded = Math.Max(overlapTime, optimalTimeNeeded);

            if (timeNeeded <= 0)
            {
                continue;
            }

            var prevWordCount = CountWords(prev.Lines);
            var nextWordCount = CountWords(next.Lines);
            var prevMinDuration = CalculateOptimalDuration(prevWordCount, wordsPerSecond, baseMinDuration, maxDuration);
            var nextMinDuration = CalculateOptimalDuration(nextWordCount, wordsPerSecond, baseMinDuration, maxDuration);

            var prevDuration = prev.EndTime - prev.StartTime;
            var nextDuration = next.EndTime - next.StartTime;
            var availableFromPrev = Math.Max(0, prevDuration - prevMinDuration);
            var availableFromNext = Math.Max(0, nextDuration - nextMinDuration);

            var timeFromPrev = Math.Min(timeNeeded / 2, availableFromPrev);
            var timeFromNext = Math.Min(timeNeeded - timeFromPrev, availableFromNext);

            var remainingNeeded = timeNeeded - timeFromPrev - timeFromNext;

            if (timeFromPrev > 0)
            {
                prev.EndTime -= timeFromPrev;
                current.StartTime -= timeFromPrev;
            }

            if (timeFromNext > 0)
            {
                next.StartTime += timeFromNext;
            }

            // If we still have overlap, adjust current subtitle timing
            switch (remainingNeeded)
            {
                case > 0 when overlapTime > 0:
                    current.EndTime = next.StartTime - buffer;
                    Console.WriteLine(
                        $"Couldn't reach optimal duration for subtitle #{current.Position} due to timing constraints");
                    break;
                case > 0 when optimalTimeNeeded > 0:
                    Console.WriteLine(
                        $"Subtitle #{current.Position} couldn't reach optimal duration of {optimalDuration}ms, achieved {current.EndTime - current.StartTime}ms");
                    break;
                default:
                {
                    if (overlapTime > 0)
                    {
                        current.EndTime = next.StartTime - buffer;
                    }
                    else if (optimalTimeNeeded > 0)
                    {
                        current.EndTime = current.StartTime + optimalDuration;
                    }

                    break;
                }
            }

            fixCount++;
            Console.WriteLine(
                $"Timing adjusted for subtitle #{current.Position} based on content length ({wordCount} words)");
        }

        if (subtitles.Count > 1)
        {
            var first = subtitles[0];
            var second = subtitles[1];
            var firstWordCount = CountWords(first.Lines);
            var firstOptimalDuration =
                CalculateOptimalDuration(firstWordCount, wordsPerSecond, baseMinDuration, maxDuration);

            var firstDuration = first.EndTime - first.StartTime;
            var hasOverlap = first.EndTime + buffer > second.StartTime;
            var isTooShort = firstDuration < firstOptimalDuration;

            if (hasOverlap || isTooShort)
            {
                var availableForward = Math.Max(0, second.StartTime - buffer - first.EndTime);

                if (first.EndTime + buffer > second.StartTime)
                {
                    first.EndTime = second.StartTime - buffer;
                    fixCount++;
                    Console.WriteLine(
                        $"Adjusted first subtitle #{first.Position} to avoid overlap with #{second.Position}");
                }
                else if (availableForward > 0 && (first.EndTime - first.StartTime) < firstOptimalDuration)
                {
                    var extensionNeeded = firstOptimalDuration - (first.EndTime - first.StartTime);
                    var extension = Math.Min(extensionNeeded, availableForward);
                    first.EndTime += extension;
                    fixCount++;
                    Console.WriteLine(
                        $"Extended first subtitle #{first.Position} duration based on content length ({firstWordCount} words)");
                }
            }

            // Last subtitle
            var lastIndex = subtitles.Count - 1;
            var last = subtitles[lastIndex];
            var secondLast = subtitles[lastIndex - 1];
            if (secondLast.EndTime + buffer > last.StartTime)
            {
                last.StartTime = secondLast.EndTime + buffer;

                if (last.EndTime - last.StartTime < baseMinDuration)
                {
                    last.EndTime = last.StartTime + baseMinDuration;
                }

                fixCount++;
                Console.WriteLine(
                    $"Adjusted last subtitle #{last.Position} to avoid overlap with #{secondLast.Position}");
            }
        }

        Console.WriteLine($"Fixed {fixCount} subtitle timings with content aware adjustments");
        return subtitles;
    }

    public bool ValidateSubtitle(string filePath, SubtitleValidationOptions options)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Cannot validate non-existent subtitle file: {FilePath}", filePath);
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return ValidateSubtitleStream(stream, Encoding.UTF8, options, Path.GetExtension(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating subtitle file: {FilePath}", filePath);
            return false;
        }
    }

    private bool ValidateSubtitleStream(
        Stream subtitleStream,
        Encoding encoding,
        SubtitleValidationOptions options,
        string? fileExtension = null)
    {
        try
        {
            if (!subtitleStream.CanSeek)
            {
                throw new ArgumentException("Stream must be seekable to validate file size.");
            }

            // Validate file size
            if (subtitleStream.Length > options.MaxFileSizeBytes)
            {
                _logger.LogWarning("Subtitle file exceeds maximum size of {MaxSize} bytes", options.MaxFileSizeBytes);
                return false;
            }

            // Reset stream position to the beginning before parsing
            subtitleStream.Seek(0, SeekOrigin.Begin);

            ISubtitleParser parser = fileExtension?.ToLowerInvariant() switch
            {
                ".ass" or ".ssa" => new SsaParser(),
                ".vtt" => new VttParser(),
                _ => new SrtParser()
            };
            var subtitles = parser.ParseStream(subtitleStream, encoding);

            if (subtitles.Count < 2)
            {
                _logger.LogWarning("Subtitle file contains less than 2 valid subtitles");
                return false;
            }

            var expectedPosition = 1;
            SubtitleItem? previousItem = null;

            foreach (var item in subtitles)
            {
                // Choose the appropriate text content based on stripSubtitleFormatting
                List<string> contentLines = options.StripSubtitleFormatting ? item.PlaintextLines : item.Lines;

                if (contentLines.Count == 0 || contentLines.All(string.IsNullOrWhiteSpace))
                {
                    _logger.LogWarning("Subtitle at position {Position} has no content", item.Position);
                    return false;
                }

                // Get the combined text for length checks
                string combinedText = string.Join(" ", contentLines);

                // Check that subtitle has at least the minimum length
                string trimmedText = combinedText.Trim();
                if (trimmedText.Length < options.MinSubtitleLength)
                {
                    _logger.LogWarning(
                        "Subtitle at position {Position} is too short. Length: {Length}, Minimum: {MinLength}",
                        item.Position, trimmedText.Length, options.MinSubtitleLength);
                    return false;
                }

                // Check sequence number/position
                if (item.Position != expectedPosition)
                {
                    _logger.LogWarning("Subtitle position mismatch. Expected: {Expected}, Found: {Found}",
                        expectedPosition, item.Position);
                    return false;
                }

                expectedPosition++;

                // Validate timing
                if (item.StartTime >= item.EndTime)
                {
                    _logger.LogWarning(
                        "Subtitle at position {Position} has invalid timing. StartTime: {StartTime}, EndTime: {EndTime}",
                        item.Position, item.StartTime, item.EndTime);
                    return false;
                }

                // Validate text length
                if (combinedText.Length > options.MaxSubtitleLength)
                {
                    _logger.LogWarning(
                        "Subtitle at position {Position} exceeds maximum length. Length: {Length}, Maximum: {MaxLength}",
                        item.Position, combinedText.Length, options.MaxSubtitleLength);
                    return false;
                }

                // Check for realistic durations
                var durationMs = item.EndTime - item.StartTime;
                if (durationMs < options.MinDurationMs || durationMs > options.MaxDurationSecs * 1000)
                {
                    _logger.LogWarning(
                        "Subtitle at position {Position} has unrealistic duration: {Duration}ms. Valid range: {MinDuration}ms to {MaxDuration}ms",
                        item.Position, durationMs, options.MinDurationMs, options.MaxDurationSecs * 1000);
                    return false;
                }

                // Check for overlapping with previous subtitle
                if (previousItem != null && item.StartTime < previousItem.EndTime)
                {
                    _logger.LogWarning(
                        "Subtitle at position {Position} overlaps with previous subtitle. Current start: {CurrentStart}, Previous end: {PreviousEnd}",
                        item.Position, item.StartTime, previousItem.EndTime);
                    return false;
                }

                // Check for control characters
                if (contentLines.Any(line => line.Any(c => char.IsControl(c) && c != '\n' && c != '\r')))
                {
                    _logger.LogWarning("Subtitle at position {Position} contains invalid control characters",
                        item.Position);
                    return false;
                }

                previousItem = item;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subtitle validation failed");
            return false;
        }
    }
    
    /// <summary>
    /// Adds an introductory subtitle at the beginning that identifies the translation service used.
    /// The intro duration is automatically adjusted to avoid overlapping with existing subtitles.
    /// </summary>
    /// <param name="serviceType">The translation service type (e.g., "openai", "google").</param>
    /// <param name="translatedSubtitles">The subtitle list to prepend the intro to.</param>
    /// <param name="translationService">The service instance used to extract model name if available.</param>
    public void AddTranslatorInfo(string serviceType, List<SubtitleItem> translatedSubtitles,
        ITranslationService translationService)
    {
        // Check if the service has a ModelName property
        var serviceName = char.ToUpper(serviceType[0]) + serviceType[1..];

        var modelField = translationService.GetType().GetField("_model",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (modelField != null)
        {
            var modelName = modelField.GetValue(translationService)?.ToString();
            if (!string.IsNullOrEmpty(modelName))
            {
                serviceName += " - " + modelName;
            }
        }

        var introText = $"# Translated with Lingarr using {serviceName} translator #";
        var introDuration = 5.0; // Default duration in seconds

        // Check if there are existing subtitles and if the first one starts before our intro ends
        if (translatedSubtitles.Count > 0)
        {
            var firstSubtitle = translatedSubtitles[0];
            var firstSubtitleStartTimeSeconds = firstSubtitle.StartTime / 1000.0;

            // If the first subtitle starts before our intro would end, adjust the intro duration
            if (firstSubtitleStartTimeSeconds < introDuration)
            {
                // Leave a small gap (e.g., 0.5 seconds) between intro and first subtitle
                introDuration = Math.Max(0.5, firstSubtitleStartTimeSeconds - 0.5);
                _logger.LogInformation(
                    "Adjusted intro duration to {introDuration} seconds to avoid overlap with first subtitle at {firstStart} seconds",
                    introDuration, firstSubtitleStartTimeSeconds);
            }
        }

        var introSubtitle = new SubtitleItem
        {
            StartTime = 0,
            EndTime = (int)(introDuration * 1000),
            Lines = [introText],
            PlaintextLines = [introText],
            TranslatedLines = [introText]
        };

        translatedSubtitles.Insert(0, introSubtitle);
    }

    /// <summary>
    /// Counts the number of words in a list of plaintext subtitle lines
    /// </summary>
    /// <param name="lines">The plaintext subtitle lines to analyze</param>
    /// <returns>The total count of words across all lines</returns>
    private static int CountWords(List<string> lines)
    {
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Sum(line => line.Split(WhitespaceCharacters, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>
    /// Calculates the optimal duration for a subtitle based on its word count
    /// </summary>
    /// <param name="wordCount">Number of words in the subtitle</param>
    /// <param name="wordsPerSecond">Reading speed in words per second</param>
    /// <param name="minDuration">Minimum allowed duration in milliseconds</param>
    /// <param name="maxDuration">Maximum allowed duration in milliseconds</param>
    /// <returns>The calculated optimal duration in milliseconds</returns>
    private static int CalculateOptimalDuration(int wordCount, double wordsPerSecond, int minDuration, int maxDuration)
    {
        var readingTime = (int)(wordCount * 1000 / wordsPerSecond);
        var optimalTime = readingTime + 500;
        return Math.Max(minDuration, Math.Min(optimalTime, maxDuration));
    }

    /// <summary>
    /// Tries to match the specified <paramref name="part"/> against any known culture/language
    /// and outputs the two-letter ISO language code if found.
    /// </summary>
    /// <param name="part">
    /// The string segment from the file name that may represent a culture or language code.
    /// </param>
    /// <param name="languageCode">
    /// When this method returns, contains the two-letter ISO language code corresponding to the 
    /// matched culture; otherwise, <c>null</c> if no match is found.
    /// </param>
    private static bool TryGetLanguageByPart(string part, out string? languageCode)
    {
        if (SubtitleLanguageHelper.TryNormalizeKnownLanguageCode(part, out var normalizedCode))
        {
            languageCode = normalizedCode;
            return true;
        }

        languageCode = null;
        return false;
    }
}
