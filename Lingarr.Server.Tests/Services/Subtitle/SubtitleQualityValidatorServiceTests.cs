using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleQualityValidatorServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly SubtitleQualityValidatorService _service;

    public SubtitleQualityValidatorServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-quality-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _service = new SubtitleQualityValidatorService(
            new SubtitleService(NullLogger<SubtitleService>.Instance),
            NullLogger<SubtitleQualityValidatorService>.Instance);
    }

    [Fact]
    public async Task ValidateAsync_WhenPolishTargetContainsChineseCluster_RejectsOutput()
    {
        var sourcePath = await WriteSrtAsync("movie.en.srt", Enumerable.Range(1, 10)
            .Select(index => $"This is source line {index}")
            .ToArray());
        var targetPath = await WriteSrtAsync("movie.pl.srt", [
            "赶紧把路修好然后离开这里",
            "你能不能关掉那不尊重人的垃圾",
            "尊重经典吧我的朋友",
            "他一定是在我们都睡着的时候修好的",
            "我们可不想他错过那场比赛",
            "那是你一生梦寐以求的奖杯",
            "我再也不用每时每刻盯着他了",
            "然后拿到那个大赞助商和直升机",
            "To jest polska linia 9",
            "To jest polska linia 10"
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.TargetLanguageMismatch, result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenSrtContainsAssDrawingResidue_RejectsOutput()
    {
        var sourcePath = await WriteSrtAsync("movie.en.srt", [
            "Hello",
            "World",
            "Again",
            "Now"
        ]);
        var targetPath = await WriteSrtAsync("movie.pl.srt", [
            "Czesc",
            "m 23.23 0 b 142.47 12.95 379.36 24.05 497",
            "m 1.43 483.19 b 2.55 466.97 4.24 450.57 6.48",
            "Teraz"
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.DrawingArtifact, result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenAssSourceIsProjectedToPlainSrt_DoesNotRequireAssTagStructure()
    {
        var sourcePath = await WriteAssAsync("episode.en.ass", [
            "{\\an7}Hello, my friend.",
            "We need {\\i1}to go{\\i0} home.",
            "{\\pos(10,20)}This is very important.",
            "Where is your sister?",
            "I cannot talk right now."
        ]);
        var targetPath = await WriteSrtAsync("episode.pl.srt", [
            "Czesc, przyjacielu.",
            "Musimy isc do domu.",
            "To jest bardzo wazne.",
            "Gdzie jest twoja siostra?",
            "Nie moge teraz rozmawiac."
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.True(result.IsValid, result.Summary);
        Assert.DoesNotContain(SubtitleQualityIssueCodes.AssTagMismatch, result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenPlainOutputDropsDrawingHeavyAssCues_UsesVisibleSourceCueCount()
    {
        var drawingCue = @"{\p1}m 0 0 l 10 10{\p0}";
        var sourcePath = await WriteAssAsync("drawing-heavy.en.ass", [
            drawingCue,
            drawingCue,
            "Hello",
            drawingCue,
            drawingCue,
            drawingCue,
            "Goodbye",
            drawingCue,
            drawingCue,
            drawingCue
        ]);
        var targetPath = await WriteSrtAsync("drawing-heavy.pl.srt", [
            "Czesc",
            "Do widzenia"
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.True(result.IsValid, result.Summary);
        Assert.Equal(2, result.SourceEntryCount);
        Assert.Equal(2, result.TargetEntryCount);
        Assert.Equal(1, result.MinimumTargetEntryCount);
    }

    [Fact]
    public async Task ValidateAsync_WhenPlainSrtUsesSequentialNumbers_ReportsMissingAssPositionsByTimestamp()
    {
        var drawingCue = @"{\p1}m 0 0 l 10 10{\p0}";
        var sourcePath = await WriteAssAsync("sparse-identity.en.ass", [
            drawingCue,
            "Hello",
            drawingCue,
            "Goodbye",
            "Another line",
            "One more line",
            "The final line"
        ]);
        var targetPath = Path.Combine(_tempDirectory, "sparse-identity.pl.srt");
        await File.WriteAllTextAsync(
            targetPath,
            "1\n00:00:02,000 --> 00:00:03,000\nCzesc\n\n" +
            "2\n00:00:04,000 --> 00:00:05,000\nDo widzenia\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nKolejna linia\n");

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.TooShort, result.IssueTypes);
        Assert.Equal(
            "Target has 3 entries but selected source has 5; minimum acceptable is 4. Missing source positions: 6, 7.",
            result.Summary);
    }

    [Fact]
    public async Task ValidateAsync_WhenAssOutputContainsDrawingCues_KeepsRawAssCounts()
    {
        var drawingCue = @"{\p1}m 0 0 l 10 10{\p0}";
        var sourceLines = Enumerable.Repeat(drawingCue, 9).Append("Hello").ToArray();
        var sourcePath = await WriteAssAsync("raw-counts.en.ass", sourceLines);
        var targetPath = await WriteAssAsync("raw-counts.pl.ass", sourceLines);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".ass"
        }, CancellationToken.None);

        Assert.Equal(10, result.SourceEntryCount);
        Assert.Equal(10, result.TargetEntryCount);
    }

    [Fact]
    public async Task ValidateAsync_WhenPlainSrtContainsAssTags_RejectsUnexpectedAssTags()
    {
        var sourcePath = await WriteAssAsync("episode.en.ass", [
            "{\\an7}Hello, my friend.",
            "We need to go home.",
            "This is very important.",
            "Where is your sister?",
            "I cannot talk right now."
        ]);
        var targetPath = await WriteSrtAsync("episode.pl.srt", [
            "{\\an7}Czesc, przyjacielu.",
            "Musimy isc do domu.",
            "To jest bardzo wazne.",
            "Gdzie jest twoja siostra?",
            "Nie moge teraz rozmawiac."
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.UnexpectedAssTags, result.IssueTypes);
        Assert.DoesNotContain(SubtitleQualityIssueCodes.AssTagMismatch, result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenPlainSrtContainsSingleAssDrawingLine_RejectsOutput()
    {
        var sourcePath = await WriteSrtAsync("movie.en.srt", [
            "Hello",
            "World",
            "Again",
            "Now"
        ]);
        var targetPath = await WriteSrtAsync("movie.pl.srt", [
            "Czesc",
            "m 0 0 280 250 l 280 285 425 285 425 250",
            "Znowu",
            "Teraz"
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.DrawingArtifact, result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenOutputLooksLikePolishTranslation_AcceptsOutput()
    {
        var sourcePath = await WriteSrtAsync("movie.en.srt", [
            "Hello, my friend.",
            "We need to go home.",
            "This is very important.",
            "Where is your sister?",
            "I cannot talk right now."
        ]);
        var targetPath = await WriteSrtAsync("movie.pl.srt", [
            "Czesc, przyjacielu.",
            "Musimy isc do domu.",
            "To jest bardzo wazne.",
            "Gdzie jest twoja siostra?",
            "Nie moge teraz rozmawiac."
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.True(result.IsValid, result.Summary);
        Assert.Empty(result.IssueTypes);
    }

    [Fact]
    public async Task ValidateAsync_WhenTargetIsTooShort_ReportsMissingPositionsAndSamples()
    {
        var sourcePath = await WriteSrtAsync("episode.en.srt", [
            "Line one",
            "♪～",
            "Line three",
            "Line four",
            "Line five",
            "Line six",
            "Line seven",
            "Line eight",
            "Line nine",
            "Line ten"
        ]);
        var targetPath = await WriteSparseSrtAsync("episode.pl.srt", [
            (1, "Linia pierwsza"),
            (3, "Linia trzecia"),
            (4, "Linia czwarta"),
            (5, "Linia piata"),
            (7, "Linia siodma")
        ]);

        var result = await _service.ValidateAsync(new SubtitleQualityValidationRequest
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLanguage = "en",
            TargetLanguage = "pl",
            OutputFormat = ".srt"
        }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(SubtitleQualityIssueCodes.TooShort, result.IssueTypes);
        Assert.Contains("Missing source positions: 2, 6, 8, 9, 10", result.Summary);
        Assert.Contains(result.SampleLines, line => line.StartsWith("2:", StringComparison.Ordinal));
        Assert.Contains(result.SampleLines, line => line.Contains("6: Line six", StringComparison.Ordinal));
    }

    private async Task<string> WriteSrtAsync(string fileName, string[] lines)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var content = string.Join(
            Environment.NewLine + Environment.NewLine,
            lines.Select((line, index) =>
                $"{index + 1}{Environment.NewLine}00:00:{index + 1:D2},000 --> 00:00:{index + 2:D2},000{Environment.NewLine}{line}"));
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<string> WriteSparseSrtAsync(string fileName, (int Position, string Line)[] cues)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var content = string.Join(
            Environment.NewLine + Environment.NewLine,
            cues.Select(cue =>
                $"{cue.Position}{Environment.NewLine}00:00:{cue.Position:D2},000 --> 00:00:{cue.Position + 1:D2},000{Environment.NewLine}{cue.Line}"));
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<string> WriteAssAsync(string fileName, string[] lines)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var events = lines.Select((line, index) =>
        {
            var start = TimeSpan.FromSeconds(index + 1);
            var end = TimeSpan.FromSeconds(index + 2);
            return $"Dialogue: 0,{start:h\\:mm\\:ss\\.ff},{end:h\\:mm\\:ss\\.ff},Default,,0,0,0,,{line}";
        });
        var content = string.Join(
            Environment.NewLine,
            [
                "[Script Info]",
                "ScriptType: v4.00+",
                "",
                "[V4+ Styles]",
                "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
                "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1",
                "",
                "[Events]",
                "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text",
                ..events
            ]);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
