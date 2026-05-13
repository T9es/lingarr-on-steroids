using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class AutoModeTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMedia_WithAutoModeAndNoConfiguredSourceLanguages_QueuesExternalTranslation()
    {
        var movie = await CreateTestMovie("auto.external.mkv");
        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/auto.external.en.srt",
                FileName = "auto.external.en",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");

        SourceSubtitleSnapshotServiceMock
            .Setup(service => service.ResolveExternalSourceWithAutoAsync(
                It.IsAny<Lingarr.Core.Interfaces.IMedia>(),
                It.IsAny<IReadOnlyCollection<Subtitles>>(),
                true,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ResolvedExternalSourceSubtitle
            {
                Subtitle = subtitles[0],
                SourceLanguage = "en",
                Snapshot = new SourceSubtitleSnapshot
                {
                    SourceType = SourceSubtitleSnapshot.ExternalType,
                    SourceLanguage = "en",
                    SourcePath = subtitles[0].Path,
                    Identity = "external|en|/movies/test/auto.external.en.srt",
                    Fingerprint = "fp:auto.external.en.srt"
                }
            });

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(request =>
                    request.SourceLanguage == "en" &&
                    request.TargetLanguage == "ro" &&
                    request.SubtitlePath == subtitles[0].Path),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithAutoModeAndNoConfiguredSourceLanguages_QueuesEmbeddedTranslation()
    {
        var movie = await CreateTestMovie("auto.embedded.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            Title = "English Full",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = false
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(1, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(request =>
                    request.MediaId == movie.Id &&
                    request.MediaType == MediaType.Movie &&
                    request.SubtitlePath == null &&
                    request.SourceLanguage == "en" &&
                    request.TargetLanguage == "ro"),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithAutoModeAndNoConfiguredSourceLanguages_QueuesOcr()
    {
        var movie = await CreateTestMovie("auto.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.NotStarted
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

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
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithStaleProcessingOcr_QueuesOcrAgain()
    {
        var movie = await CreateTestMovie("stale.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Processing,
            OcrAttemptedAt = DateTime.UtcNow.AddDays(-1)
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

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
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                embeddedSubtitle.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithFreshProcessingOcr_DoesNotQueueDuplicateOcr()
    {
        var movie = await CreateTestMovie("fresh.ocr.mkv");
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Processing,
            OcrAttemptedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);

        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "ro", Name = "Romanian" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }
}
