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

public class EmbeddedSubtitleSelectionTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessMediaForceAsync_WithSignsAndDialogueTracks_UsesBestConfiguredLanguage()
    {
        // Arrange
        var movie = await CreateTestMovie();

        var embeddedSubs = new List<EmbeddedSubtitle>
        {
            new()
            {
                MovieId = movie.Id,
                StreamIndex = 0,
                Language = "eng",
                Title = "Signs & Songs [KH]",
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
                Title = "Full Subtitles [Foxtrot]",
                CodecName = "ass",
                IsTextBased = true,
                IsDefault = false,
                IsForced = false
            }
        };

        movie.EmbeddedSubtitles.AddRange(embeddedSubs);
        await DbContext.EmbeddedSubtitles.AddRangeAsync(embeddedSubs);
        await DbContext.SaveChangesAsync();

        // No external subtitles so the processor will fall back to embedded subtitles
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

        // Act
        var queued = await Processor.ProcessMediaForceAsync(movie, MediaType.Movie);

        // Assert
        Assert.Equal(1, queued);

        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.Is<TranslateAbleSubtitle>(t =>
                t.MediaId == movie.Id &&
                t.MediaType == MediaType.Movie &&
                t.SubtitlePath == null &&
                t.SourceLanguage == "ja" &&
                t.TargetLanguage == "ro"), It.IsAny<bool>()),
            Times.Once);
    }
}

