using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
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
            .Setup(s => s.GetAllSubtitles(movie.Path))
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
}
