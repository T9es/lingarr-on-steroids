using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

/// <summary>
/// Covers the last-resort promotion of text subtitle tracks that releases incorrectly flag as
/// forced. Such tracks have no extracted entry count, so normal selection rejects them as
/// supplemental and the media stays pending without ever producing a request. Promotion must
/// require a healthy, full-dialogue, language-consistent extraction.
/// </summary>
public class ForcedDialoguePromotionTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_PromotesForcedTrackWithVerifiedFullDialogue()
    {
        var movie = await CreateTestMovie("forced.full.mkv");
        var embeddedSubtitle = await AddForcedEmbeddedSubtitle(movie, title: "English");
        var extractedPath = CreateSrtFile(60);

        SetupPromotionSettings();
        SetupSubtitleServices(EnglishCues(60));
        SubtitleExtractionServiceMock
            .Setup(service => service.ExtractSubtitle(
                It.IsAny<string>(),
                embeddedSubtitle.StreamIndex,
                It.IsAny<string>(),
                embeddedSubtitle.CodecName,
                embeddedSubtitle.Language))
            .ReturnsAsync(extractedPath);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(1, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(
                It.Is<TranslateAbleSubtitle>(request =>
                    request.MediaId == movie.Id &&
                    request.MediaType == MediaType.Movie &&
                    request.SubtitlePath == null &&
                    request.SourceLanguage == "en" &&
                    request.TargetLanguage == "pl" &&
                    request.SourceSubtitleType == SubtitleLanguageHelper.TypeForcedDialogue &&
                    request.IsForcedSubtitle),
                It.IsAny<bool>()),
            Times.Once);
        Assert.False(File.Exists(extractedPath), "The temporary promotion extraction should be deleted.");
    }

    [Fact]
    public async Task ProcessMediaForceAsync_DoesNotPromoteForcedTrackWithTooFewDialogueEntries()
    {
        var movie = await CreateTestMovie("forced.sparse.mkv");
        var embeddedSubtitle = await AddForcedEmbeddedSubtitle(movie, title: "English");
        var extractedPath = CreateSrtFile(30);

        SetupPromotionSettings();
        SetupSubtitleServices(EnglishCues(30));
        SubtitleExtractionServiceMock
            .Setup(service => service.ExtractSubtitle(
                It.IsAny<string>(),
                embeddedSubtitle.StreamIndex,
                It.IsAny<string>(),
                embeddedSubtitle.CodecName,
                embeddedSubtitle.Language))
            .ReturnsAsync(extractedPath);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_DoesNotPromoteForcedTypesettingTrackWithRepeatedFragments()
    {
        var movie = await CreateTestMovie("forced.typesetting.mkv");
        var embeddedSubtitle = await AddForcedEmbeddedSubtitle(movie, title: "English");
        var extractedPath = CreateSrtFile(60);

        var repeatedFragments = Enumerable.Range(1, 60)
            .Select(index => new SubtitleItem
            {
                Position = index,
                Lines = [$"Karaoke caption fragment {index % 20}."],
                PlaintextLines = [$"Karaoke caption fragment {index % 20}."]
            })
            .ToList();

        SetupPromotionSettings();
        SetupSubtitleServices(repeatedFragments);
        SubtitleExtractionServiceMock
            .Setup(service => service.ExtractSubtitle(
                It.IsAny<string>(),
                embeddedSubtitle.StreamIndex,
                It.IsAny<string>(),
                embeddedSubtitle.CodecName,
                embeddedSubtitle.Language))
            .ReturnsAsync(extractedPath);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_DoesNotPromoteForcedSignsAndSongsTitle()
    {
        var movie = await CreateTestMovie("forced.signs.mkv");
        var embeddedSubtitle = await AddForcedEmbeddedSubtitle(movie, title: "Signs & Songs");
        var extractedPath = CreateSrtFile(60);

        SetupPromotionSettings();
        SetupSubtitleServices(EnglishCues(60));
        SubtitleExtractionServiceMock
            .Setup(service => service.ExtractSubtitle(
                It.IsAny<string>(),
                embeddedSubtitle.StreamIndex,
                It.IsAny<string>(),
                embeddedSubtitle.CodecName,
                embeddedSubtitle.Language))
            .ReturnsAsync(extractedPath);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_DoesNotPromoteForcedTrackWhoseTextContradictsDeclaredLanguage()
    {
        var movie = await CreateTestMovie("forced.wronglang.mkv");
        var embeddedSubtitle = await AddForcedEmbeddedSubtitle(movie, title: "English");
        var extractedPath = CreateSrtFile(60, _ => "これは日本語の字幕です。彼は門のところへ行きます。");

        SetupPromotionSettings();
        SetupSubtitleServices(JapaneseCues(60));
        SubtitleExtractionServiceMock
            .Setup(service => service.ExtractSubtitle(
                It.IsAny<string>(),
                embeddedSubtitle.StreamIndex,
                It.IsAny<string>(),
                embeddedSubtitle.CodecName,
                embeddedSubtitle.Language))
            .ReturnsAsync(extractedPath);

        var queued = await Processor.ProcessMediaForceAsync(
            movie,
            MediaType.Movie,
            forceProcess: true,
            forceTranslation: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()),
            Times.Never);
    }

    private async Task<EmbeddedSubtitle> AddForcedEmbeddedSubtitle(Movie movie, string title)
    {
        var embeddedSubtitle = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 0,
            Language = "eng",
            Title = title,
            CodecName = "subrip",
            IsTextBased = true,
            IsForced = true,
            IsExtracted = false
        };

        movie.EmbeddedSubtitles.Add(embeddedSubtitle);
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSubtitle);
        await DbContext.SaveChangesAsync();
        return embeddedSubtitle;
    }

    private void SetupPromotionSettings()
    {
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync([]);
        SettingServiceMock
            .Setup(service => service.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync([new TargetLanguage { Code = "pl", Name = "Polish" }]);
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SourceLanguageMode))
            .ReturnsAsync("auto");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.IgnoreCaptions))
            .ReturnsAsync("false");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleOutputMode))
            .ReturnsAsync("srt-only");
        SettingServiceMock
            .Setup(service => service.GetSetting(SettingKeys.Translation.TranslateSupplementalSubtitles))
            .ReturnsAsync("false");
    }

    private void SetupSubtitleServices(IReadOnlyList<SubtitleItem> cues)
    {
        SubtitleServiceMock
            .Setup(service => service.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SubtitleServiceMock
            .Setup(service => service.ReadSubtitles(It.IsAny<string>()))
            .ReturnsAsync(() => cues.ToList());
        SubtitleExtractionServiceMock
            .Setup(service => service.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);
        TranslationRequestServiceMock
            .Setup(service => service.HasExistingNonSupplementalRequestAsync(
                It.IsAny<int>(),
                It.IsAny<MediaType>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);
        TranslationRequestServiceMock
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()))
            .ReturnsAsync(1);
    }

    private static List<SubtitleItem> EnglishCues(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new SubtitleItem
            {
                Position = index,
                Lines = [EnglishLine(index)],
                PlaintextLines = [EnglishLine(index)]
            })
            .ToList();
    }

    private static List<SubtitleItem> JapaneseCues(int count)
    {
        const string line = "これは日本語の字幕です。彼は門のところへ行きます。";
        return Enumerable.Range(1, count)
            .Select(index => new SubtitleItem
            {
                Position = index,
                Lines = [line],
                PlaintextLines = [line]
            })
            .ToList();
    }

    private static string EnglishLine(int index)
    {
        return $"This is the English line number {index} and we are going to the gate with them " +
               "because it is not safe to stay here.";
    }

    private static string CreateSrtFile(int cueCount, Func<int, string>? textFactory = null)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "lingarr-forced-promotion-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "promoted.en.srt");
        var builder = new StringBuilder();

        for (var index = 1; index <= cueCount; index++)
        {
            var start = TimeSpan.FromSeconds(index * 5);
            var end = start.Add(TimeSpan.FromSeconds(2));
            builder.Append(index).Append('\n');
            builder.Append(start.ToString(@"hh\:mm\:ss\,fff")).Append(" --> ");
            builder.Append(end.ToString(@"hh\:mm\:ss\,fff")).Append('\n');
            builder.Append(textFactory?.Invoke(index) ?? EnglishLine(index)).Append("\n\n");
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }
}
