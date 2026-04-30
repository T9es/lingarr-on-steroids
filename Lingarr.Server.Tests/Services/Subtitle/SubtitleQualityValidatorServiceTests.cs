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
    public async Task ValidateAsync_WhenTargetEchoesEnglishSourceForPolish_RejectsOutput()
    {
        var sourcePath = await WriteSrtAsync("movie.en.srt", [
            "OK... Here we go. Focus.",
            "Speed. I am speed.",
            "One winner, 42 losers.",
            "I eat losers for breakfast.",
            "I am faster than fast."
        ]);
        var targetPath = await WriteSrtAsync("movie.pl.srt", [
            "OK... Here we go. Focus.",
            "Speed. I am speed.",
            "One winner, 42 losers.",
            "I eat losers for breakfast.",
            "I am faster than fast."
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
        Assert.Contains(SubtitleQualityIssueCodes.UnchangedSourceText, result.IssueTypes);
        Assert.NotEmpty(result.SampleLines);
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
