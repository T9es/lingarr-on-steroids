using System.Collections.Generic;
using System.IO;
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

public class AutoScanFallbackTests : MediaSubtitleProcessorTestBase
{
    [Fact]
    public async Task ProcessSubtitles_ShouldFallbackToEmbedded_AndValidatingExistingTarget_WhenExternalSourceMissing()
    {
        // Arrange
        var movie = await CreateTestMovie("movie_autoscan.mkv");
        
        // Add embedded subtitles to the movie in DB
        var embeddedSub = new EmbeddedSubtitle
        {
            MovieId = movie.Id,
            StreamIndex = 1,
            Language = "eng",
            CodecName = "subrip",
            IsTextBased = true,
            Title = "English",
            IsForced = false
        };
        await DbContext.EmbeddedSubtitles.AddAsync(embeddedSub);
        await DbContext.SaveChangesAsync();
        
        // Setup Settings: Source=en, Target=pl
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
            .Setup(s => s.GetSetting(SettingKeys.SubtitleValidation.IntegrityValidationEnabled))
            .ReturnsAsync("true");

        // Setup External Subtitles: Only Target (pl) exists, Source (en) is MISSING
        var externalSubtitles = new List<Subtitles>
        {
            new() { FileName = "movie_autoscan.mkv.pl.srt", Path = "/movies/test/movie_autoscan.mkv.pl.srt", Language = "pl", Format = "srt" }
        };

        // Setup Extraction Service to simulate successful temp extraction
        var tempPath = Path.Combine(Path.GetTempPath(), "simulated_temp.srt");
        // Ensure we don't accidentally rely on file existence in test unless we file mock it, 
        // but IntegrityService checks File.Exists using real IO?
        // Wait, IntegrityService is MOCKED in base class. 
        // We just need to check if IntegrityService.ValidateIntegrityAsync IS CALLED with the temp path.
        
        SubtitleExtractionServiceMock
            .Setup(x => x.ExtractSubtitle(It.IsAny<string>(), 1, It.IsAny<string>(), "srt", "en"))
            .ReturnsAsync(tempPath);

        // Setup Integrity Service to RETURN FALSE (Invalid) -> Should trigger Re-Translation
        SubtitleIntegrityServiceMock
            .Setup(x => x.ValidateIntegrityAsync(tempPath, externalSubtitles[0].Path))
            .ReturnsAsync(false); // Fails validation!

        // Act
        // ProcessSubtitles is private, called by ProcessMediaAsync.
        // ProcessMediaAsync calls ProbeMedia -> ProcessSubtitles.
        // We'll call ProcessMediaAsync(forceProcess: true) to bypass hash check, 
        // enforcing it runs ProcessSubtitles logic (Auto Scan path, NOT Manual Force path which is ProcessSubtitlesWithCount).
        // Wait, ProcessMediaAsync calls ProcessSubtitles? Yes.
        // ProcessMediaForceAsync calls ProcessSubtitlesWithCount.
        // To test Auto Scan, we call ProcessMediaAsync, but we need to mock ProbeMedia returning the external subs.
        
        SubtitleServiceMock
            .Setup(s => s.GetAllSubtitles(movie.Path))
            .ReturnsAsync(externalSubtitles);

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        // Assert
        // Should have called ProbeEmbedded -> Extracted Temp -> Validated (Failed) -> Queued Repair.
        
        // 1. Verify Probe (Extraction Service Sync) called
        SubtitleExtractionServiceMock.Verify(x => x.SyncEmbeddedSubtitles(It.IsAny<Movie>()), Times.Once);
        
        // 2. Verify Extract Temp
        SubtitleExtractionServiceMock.Verify(x => x.ExtractSubtitle(It.IsAny<string>(), 1, It.IsAny<string>(), "srt", "en"), Times.Once);
        
        // 3. Verify Integrity Check with Temp File
        SubtitleIntegrityServiceMock.Verify(x => x.ValidateIntegrityAsync(tempPath, externalSubtitles[0].Path), Times.Once);
        
        // 4. Verify Translation Request Queued (Because Integrity Failed)
        TranslationRequestServiceMock.Verify(x => x.CreateRequest(
            It.Is<TranslateAbleSubtitle>(r => 
                r.SourceLanguage == "en" && 
                r.TargetLanguage == "pl" && 
                r.SubtitlePath == null // Should be null if queueing from embedded? 
                // Wait, logic says: SubtitlePath = (tempSourcePath != null) ? null : sourceSubtitle.Path
                // So yes, should be NULL.
            ), 
            It.IsAny<bool>()), 
            Times.Once);
    }
}
