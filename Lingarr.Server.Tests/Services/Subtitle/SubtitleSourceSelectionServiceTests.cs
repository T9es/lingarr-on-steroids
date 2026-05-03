using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleSourceSelectionServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ISubtitleService> _subtitleServiceMock = new();

    public SubtitleSourceSelectionServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-source-selection-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SelectPrimaryAsync_RejectsTitleForcedTrackEvenWhenForcedFlagIsFalse()
    {
        var service = CreateService();

        var result = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "Forced",
                    CodecName = "ass",
                    IsTextBased = true,
                    IsForced = false
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 2,
                    Language = "eng",
                    Title = "English",
                    CodecName = "subrip",
                    IsTextBased = true,
                    IsForced = false
                }
            ],
            ["en"],
            allowCaptionFallback: true);

        Assert.NotNull(result.SelectedSubtitle);
        Assert.Equal(2, result.SelectedSubtitle!.StreamIndex);
        Assert.Equal(SubtitleSourceCandidateRole.SupplementalForcedSigns, GetRole(result, 0));
    }

    [Fact]
    public async Task SelectPrimaryAsync_RejectsSparseHigherPriorityTrackAndUsesLowerPriorityFullTrack()
    {
        var sparsePath = await WriteSrtAsync("sparse.en.srt", 32);
        var service = CreateService();

        var result = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English",
                    CodecName = "subrip",
                    IsTextBased = true,
                    ExtractedPath = sparsePath
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 1,
                    Language = "jpn",
                    Title = "Japanese Full",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            ["en", "ja"],
            allowCaptionFallback: true);

        Assert.NotNull(result.SelectedSubtitle);
        Assert.Equal(1, result.SelectedSubtitle!.StreamIndex);
        Assert.Equal("ja", result.MatchedLanguage);
        Assert.Equal(SubtitleSourceCandidateRole.RejectedSparse, GetRole(result, 0));
    }

    [Fact]
    public async Task SelectPrimaryAsync_RejectsZeroDialogueAssTrack()
    {
        var assPath = Path.Combine(_tempDirectory, "empty.en.ass");
        await File.WriteAllTextAsync(
            assPath,
            """
            [Script Info]
            Title: Empty

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            """);
        var service = CreateService();

        var result = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English",
                    CodecName = "ass",
                    IsTextBased = true,
                    ExtractedPath = assPath
                }
            ],
            ["en"],
            allowCaptionFallback: true);

        Assert.Null(result.SelectedSubtitle);
        Assert.Equal(SubtitleSourceCandidateRole.RejectedSparse, GetRole(result, 0));
    }

    [Fact]
    public async Task SelectPrimaryAsync_RejectsTimingOnlySrtTrackAndUsesLowerPriorityFullTrack()
    {
        var timingOnlyPath = await WriteTimingOnlySrtAsync("timing-only.en.srt", 60);
        var service = CreateService();

        var result = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English",
                    CodecName = "subrip",
                    IsTextBased = true,
                    ExtractedPath = timingOnlyPath
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 1,
                    Language = "jpn",
                    Title = "Japanese Full",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            ["en", "ja"],
            allowCaptionFallback: true);

        Assert.NotNull(result.SelectedSubtitle);
        Assert.Equal(1, result.SelectedSubtitle!.StreamIndex);
        Assert.Equal("ja", result.MatchedLanguage);
        Assert.Equal(SubtitleSourceCandidateRole.RejectedSparse, GetRole(result, 0));
    }

    [Fact]
    public async Task SelectPrimaryAsync_RejectsPathologicalAssTrackAndUsesCleanSrt()
    {
        var assPath = Path.Combine(_tempDirectory, "pathological.en.ass");
        await File.WriteAllTextAsync(assPath, "[Events]\n" + string.Join(
            "\n",
            Enumerable.Range(1, 600).Select(index =>
                $"Dialogue: 0,0:00:{index % 60:00}.00,0:00:{index % 60:00}.50,Default,,0,0,0,,{{\\p1}}m 0 0 l 10 10")));

        _subtitleServiceMock
            .Setup(service => service.ReadSubtitles(assPath))
            .ReturnsAsync(Enumerable.Range(1, 600)
                .Select(index => new SubtitleItem
                {
                    Position = index,
                    Lines = ["{\\p1}m 0 0 l 10 10"],
                    PlaintextLines = [""]
                })
                .ToList());

        var service = CreateService();

        var result = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "Full Subtitles",
                    CodecName = "ass",
                    IsTextBased = true,
                    ExtractedPath = assPath
                },
                new EmbeddedSubtitle
                {
                    StreamIndex = 1,
                    Language = "eng",
                    Title = "English",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            ["en"],
            allowCaptionFallback: true);

        Assert.NotNull(result.SelectedSubtitle);
        Assert.Equal(1, result.SelectedSubtitle!.StreamIndex);
        Assert.Equal(SubtitleSourceCandidateRole.RejectedPathological, GetRole(result, 0));
    }

    [Fact]
    public async Task SelectPrimaryAsync_UsesCaptionFallbackOnlyWhenAllowedAndNoCleanSourceExists()
    {
        var service = CreateService();

        var allowed = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English SDH",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            ["en"],
            allowCaptionFallback: true);

        var disallowed = await service.SelectPrimaryAsync(
            [
                new EmbeddedSubtitle
                {
                    StreamIndex = 0,
                    Language = "eng",
                    Title = "English SDH",
                    CodecName = "subrip",
                    IsTextBased = true
                }
            ],
            ["en"],
            allowCaptionFallback: false);

        Assert.Equal(0, allowed.SelectedSubtitle?.StreamIndex);
        Assert.Equal(SubtitleSourceCandidateRole.CaptionFallback, allowed.SelectedRole);
        Assert.Null(disallowed.SelectedSubtitle);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private SubtitleSourceSelectionService CreateService()
    {
        return new SubtitleSourceSelectionService(
            _subtitleServiceMock.Object,
            NullLogger<SubtitleSourceSelectionService>.Instance);
    }

    private async Task<string> WriteSrtAsync(string fileName, int cueCount)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var content = string.Join(
            "\n\n",
            Enumerable.Range(1, cueCount).Select(index =>
                $"{index}\n00:00:{index % 60:00},000 --> 00:00:{index % 60:00},500\nLine {index}"));
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<string> WriteTimingOnlySrtAsync(string fileName, int cueCount)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var content = string.Join(
            "\n\n",
            Enumerable.Range(1, cueCount).Select(index =>
                $"{index}\n00:00:{index % 60:00},000 --> 00:00:{index % 60:00},500"));
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static SubtitleSourceCandidateRole GetRole(
        SubtitleSourceSelectionResult result,
        int streamIndex)
    {
        return result.Assessments
            .Single(assessment => assessment.Subtitle.StreamIndex == streamIndex)
            .Role;
    }
}
