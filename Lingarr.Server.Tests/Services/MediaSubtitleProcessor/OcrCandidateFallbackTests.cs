using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class OcrCandidateFallbackTests : MediaSubtitleProcessorTestBase
{
    [Theory]
    [InlineData("manual")]
    [InlineData("auto")]
    [InlineData("untagged")]
    public async Task ProcessMediaForceAsync_PrefersUnprocessedAlternativeToRetainedBlockedOutput(string mode)
    {
        var blockedPath = Path.Combine(Path.GetTempPath(), $"lingarr-ocr-{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(blockedPath, "Blocked OCR output");
        try
        {
            var movie = await PrepareMovieAsync(mode, blockedPath, SubtitleOcrStatus.NotStarted);

            await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

            VerifyQueued(movie.Id, 1, Times.Once());
            VerifyQueued(movie.Id, 0, Times.Never());
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    [Theory]
    [InlineData("manual", SubtitleOcrStatus.Queued)]
    [InlineData("auto", SubtitleOcrStatus.Queued)]
    [InlineData("manual", SubtitleOcrStatus.Processing)]
    [InlineData("auto", SubtitleOcrStatus.Processing)]
    public async Task ProcessMediaForceAsync_WaitsForInFlightAlternativeWithoutDuplicateOcr(
        string mode, SubtitleOcrStatus status)
    {
        var blockedPath = Path.Combine(Path.GetTempPath(), $"lingarr-ocr-{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(blockedPath, "Blocked OCR output");
        try
        {
            var movie = await PrepareMovieAsync(mode, blockedPath, status);

            await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

            SubtitleOcrServiceMock.Verify(service => service.QueueOcrAsync(
                movie.Id, MediaType.Movie, It.IsAny<int>(), false, It.IsAny<CancellationToken>()), Times.Never());
            Assert.Empty(BackgroundJobClientMock.Invocations);
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    [Theory]
    [InlineData("manual", "language")]
    [InlineData("auto", "forced")]
    [InlineData("auto", "caption")]
    [InlineData("manual", "absent")]
    public async Task ProcessMediaForceAsync_RetainsBlockedReevaluationWhenAlternativeIsIneligible(
        string mode, string exclusion)
    {
        var blockedPath = Path.Combine(Path.GetTempPath(), $"lingarr-ocr-{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(blockedPath, "Blocked OCR output");
        try
        {
            var movie = await PrepareMovieAsync(mode, blockedPath, SubtitleOcrStatus.NotStarted);
            var alternative = movie.EmbeddedSubtitles.Find(subtitle => subtitle.StreamIndex == 1)!;
            if (exclusion == "absent")
            {
                DbContext.EmbeddedSubtitles.Remove(alternative);
                movie.EmbeddedSubtitles.Remove(alternative);
            }
            else
            {
                alternative.Language = exclusion == "language" ? "fra" : "eng";
                alternative.IsForced = exclusion == "forced";
                alternative.Title = exclusion == "caption" ? "English SDH" : "English";
            }
            await DbContext.SaveChangesAsync();

            await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

            VerifyQueued(movie.Id, 0, Times.Once());
            VerifyQueued(movie.Id, 1, Times.Never());
            Assert.Empty(BackgroundJobClientMock.Invocations);
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    private async Task<Movie> PrepareMovieAsync(
        string mode, string blockedPath, SubtitleOcrStatus alternativeStatus)
    {
        var movie = await CreateTestMovie("ocr.fallback.mkv");
        var language = mode == "untagged" ? "und" : "eng";
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = language,
            Title = "Full English",
            IsDefault = true,
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
            OcrQualityScore = 30,
            OcrExtractedPath = blockedPath
        });
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = language,
            Title = "English",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = alternativeStatus,
            OcrAttemptedAt = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();
        SetupStandardSettings();
        SettingServiceMock.Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync(mode == "auto" ? "auto" : "manual");
        SettingServiceMock.Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock.Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");
        SubtitleServiceMock.Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock.Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock.Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock.Setup(service => service.QueueOcrAsync(
                movie.Id, MediaType.Movie, It.IsAny<int>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, MediaType _, int streamIndex, bool _, CancellationToken _) =>
                new SubtitleOcrResult
                {
                    Success = streamIndex == 1,
                    Status = streamIndex == 1 ? SubtitleOcrStatus.Queued : SubtitleOcrStatus.BlockedLowQuality
                });
        return movie;
    }

    [Fact]
    public async Task ProcessMediaForceAsync_QueuesForcedBitmapSourceWhenNoOrdinaryBitmapSourceExists()
    {
        var blockedPath = Path.Combine(Path.GetTempPath(), $"lingarr-ocr-{Guid.NewGuid():N}.srt");
        await File.WriteAllTextAsync(blockedPath, "Blocked OCR output");
        try
        {
            var movie = await CreateTestMovie("ocr.forced.mkv");
            movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "eng",
                Title = "Forced",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false,
                IsForced = true,
                OcrStatus = SubtitleOcrStatus.BlockedLowQuality,
                OcrQualityScore = 30,
                OcrExtractedPath = blockedPath,
                OcrAttemptedAt = DateTime.UtcNow
            });
            movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
            {
                MovieId = movie.Id,
                StreamIndex = 1,
                Language = "eng",
                Title = "Foreign",
                CodecName = "hdmv_pgs_subtitle",
                IsTextBased = false,
                IsForced = true,
                OcrStatus = SubtitleOcrStatus.NotStarted,
                OcrAttemptedAt = DateTime.UtcNow
            });
            await DbContext.SaveChangesAsync();
            SetupOcrProcessing(movie.Id);

            await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

            // Candidates keep the ordinary ordering: an unprocessed track is preferred over
            // a retained blocked output even though the blocked track has a lower stream index.
            VerifyQueued(movie.Id, 1, Times.Once());
            VerifyQueued(movie.Id, 0, Times.Never());
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    [Fact]
    public async Task ProcessMediaForceAsync_PrefersOrdinaryBitmapSourceOverForcedBitmapSource()
    {
        var movie = await CreateTestMovie("ocr.forced.preference.mkv");
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = "Forced",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            IsForced = true,
            OcrStatus = SubtitleOcrStatus.NotStarted,
            OcrAttemptedAt = DateTime.UtcNow
        });
        movie.EmbeddedSubtitles.Add(new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            Title = "Full English",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.NotStarted,
            OcrAttemptedAt = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();
        SetupOcrProcessing(movie.Id);

        await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        VerifyQueued(movie.Id, 1, Times.Once());
        VerifyQueued(movie.Id, 0, Times.Never());
    }

    private void SetupOcrProcessing(int movieId)
    {
        SetupStandardSettings();
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("manual");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");
        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock
            .Setup(service => service.QueueOcrAsync(
                movieId, MediaType.Movie, It.IsAny<int>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });
    }

    private void VerifyQueued(int mediaId, int streamIndex, Times times)
    {
        SubtitleOcrServiceMock.Verify(service => service.QueueOcrAsync(
            mediaId, MediaType.Movie, streamIndex, false, It.IsAny<CancellationToken>()), times);
    }
}
