using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class DuplicationPreventionTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_WithExistingPendingExternalRequest_DoesNotEnqueueDuplicate()
    {
        var movie = await CreateTestMovie();
        SetupStandardSettings();

        var subtitles = new List<Subtitles>
        {
            new()
            {
                Path = "/movies/test/test.movie.en.srt",
                FileName = "test.movie.en",
                Language = "en",
                Caption = "",
                Format = ".srt"
            }
        };

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(subtitles);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = movie.Title,
            SourceLanguage = "en",
            TargetLanguage = "ro",
            SubtitleToTranslate = subtitles[0].Path,
            Status = TranslationStatus.Pending
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie, forceProcess: false);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMediaForceAsync_WithExistingPendingEmbeddedRequest_DoesNotEnqueueDuplicate()
    {
        var movie = await CreateTestMovie();

        var embeddedSubs = new List<EmbeddedSubtitle>
        {
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "eng",
                Title = "Signs & Songs",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = true,
                IsForced = true
            },
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 1,
                Language = "jpn",
                Title = "Full Subtitles",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = false,
                IsForced = false
            }
        };

        movie.EmbeddedSubtitles.AddRange(embeddedSubs);
        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedSubs);
        await DbContext.SaveChangesAsync();

        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<SourceLanguage>(SettingKeys.Translation.SourceLanguages))
            .ReturnsAsync(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English" },
                new() { Code = "ja", Name = "Japanese" }
            });

        SettingServiceMock
            .Setup(s => s.GetSettingAsJson<TargetLanguage>(SettingKeys.Translation.TargetLanguages))
            .ReturnsAsync(new List<TargetLanguage>
            {
                new() { Code = "ro", Name = "Romanian" }
            });

        SubtitleExtractionServiceMock
            .Setup(s => s.SyncEmbeddedSubtitles(It.IsAny<Movie>()))
            .Returns(Task.CompletedTask);

        DbContext.TranslationRequests.Add(new TranslationRequest
        {
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = movie.Title,
            SourceLanguage = "ja",
            TargetLanguage = "ro",
            SubtitleToTranslate = null,
            Status = TranslationStatus.Pending
        });
        await DbContext.SaveChangesAsync();

        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie);

        Assert.Equal(0, queued);
        TranslationRequestServiceMock.Verify(s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>(), It.IsAny<bool>()), Times.Never);
    }
}

