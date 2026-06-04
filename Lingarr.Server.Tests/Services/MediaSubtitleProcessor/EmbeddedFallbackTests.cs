using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Subtitle;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class EmbeddedFallbackTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_ShouldFallbackToEmbedded_WhenExternalSourceMissing()
    {
        // Arrange
        var movie = await CreateTestMovie("movie.mkv");
        
        // Add embedded subtitles to the movie in DB
        var embeddedSub = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true,
            Title = "English"
        };
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSub);
        await DbContext.SaveChangesAsync();
        
        // Setup Settings: Source=en, Target=fr
        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage> { new() { Code = "en", Name = "English" } });
        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage> { new() { Code = "fr", Name = "French" } });
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");

        // Setup External Subtitles: Only Target (fr) exists, Source (en) is MISSING
        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(movie.Path!))
            .ReturnsAsync(new List<Subtitles>
            {
                new() { FileName = "movie.mkv.fr.srt", Path = "/movies/test/movie.mkv.fr.srt", Language = "fr", Format = "srt" }
            });

        // Act
        var result = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        // Assert
        // Logic should fallback to embedded because En external is missing.
        // It should match embedded 'eng' with config 'en'.
        // It should queue 1 translation (en -> fr).
        
        Assert.Equal(1, result);
        
        TranslationRequestServiceMock.Verify(x => x.CreateRequest(
            It.Is<TranslateAbleSubtitle>(r => 
                r.SourceLanguage == "en" && 
                r.TargetLanguage == "fr" && 
                r.SubtitlePath == null // Null implies embedded extraction
            ), 
            It.IsAny<bool>()), 
            Times.Once);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithReadableTargetButNoReadableSource_QueuesBitmapSourceOcr()
    {
        var movie = await CreateTestMovie("korra.s04e03.mkv");
        var embeddedTarget = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "pol",
            Title = "pl (Lingarr)",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = true
        };
        var bitmapSource = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.NotStarted
        };

        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedTarget, bitmapSource);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(movie.Path!))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock
            .Setup(service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                bitmapSource.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage> { new() { Code = "en", Name = "English" } });
        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage> { new() { Code = "pl", Name = "Polish" } });
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                bitmapSource.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
        var updatedMovie = await DbContext.Movies.FindAsync(movie.Id);
        Assert.Null(updatedMovie?.MediaHash);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithReadableTargetAndMissingCompletedOcrOutput_ResetsAndQueuesOcr()
    {
        var movie = await CreateTestMovie("korra.s04e12.mkv");
        var missingOcrPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{System.Guid.NewGuid():N}.srt");
        var embeddedTarget = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "pol",
            Title = "pl (Lingarr)",
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = true
        };
        var staleBitmapSource = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 2,
            Language = "eng",
            Title = "English PGS",
            CodecName = "hdmv_pgs_subtitle",
            IsTextBased = false,
            OcrStatus = SubtitleOcrStatus.Succeeded,
            OcrExtractedPath = missingOcrPath,
            OcrCueCount = 250,
            OcrQualityScore = 92
        };

        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedTarget, staleBitmapSource);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(movie.Path!))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        SubtitleOcrServiceMock
            .Setup(service => service.IsSupportedCodec("hdmv_pgs_subtitle"))
            .Returns(true);
        SubtitleOcrServiceMock
            .Setup(service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                staleBitmapSource.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new SubtitleOcrResult
            {
                Success = true,
                Status = SubtitleOcrStatus.Queued
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage> { new() { Code = "en", Name = "English" } });
        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage> { new() { Code = "pl", Name = "Polish" } });
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleExtraction.OcrEnabled))
            .ReturnsAsync("true");
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.SubtitleExtraction.OcrAutoQueue))
            .ReturnsAsync("true");

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: true);

        Assert.Equal(0, queued);
        SubtitleOcrServiceMock.Verify(
            service => service.QueueOcrAsync(
                movie.Id,
                MediaType.Movie,
                staleBitmapSource.StreamIndex,
                false,
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);

        var updatedSubtitle = await DbContext.EmbeddedSubtitles.FindAsync(staleBitmapSource.Id);
        Assert.Equal(SubtitleOcrStatus.NotStarted, updatedSubtitle?.OcrStatus);
        Assert.Null(updatedSubtitle?.OcrExtractedPath);
        Assert.Null(updatedSubtitle?.OcrCueCount);
        Assert.Null(updatedSubtitle?.OcrQualityScore);
    }
}
