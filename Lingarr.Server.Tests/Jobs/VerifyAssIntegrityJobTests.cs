using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Subtitle;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class VerifyAssIntegrityJobTests
{
    [Fact]
    public async Task Execute_RepairsDamagedGeneratedSrtSidecarFromTranslatedAss()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = tempDirectory.Path,
            DateAdded = DateTime.UtcNow
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var sourceAssPath = Path.Combine(tempDirectory.Path, "movie.mkv.en.ass");
        var translatedAssPath = Path.Combine(tempDirectory.Path, "movie.mkv.pl.lingarr.ass");
        var translatedSrtPath = Path.Combine(tempDirectory.Path, "movie.mkv.pl.lingarr.srt");
        await File.WriteAllTextAsync(sourceAssPath, BuildAssContent("Hello"));
        await File.WriteAllTextAsync(
            translatedAssPath,
            BuildAssContent("Czesc", secondText: "m 0 0 l 1 0 1 1 0 1"));
        await File.WriteAllTextAsync(translatedSrtPath, BuildDamagedSrtContent());

        context.TranslationRequests.Add(new TranslationRequest
        {
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = $"library:Movie:{movie.Id}:ass",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourceAssPath,
            TranslatedSubtitle = translatedAssPath,
            SourceSubtitleFormat = ".ass",
            RequiredOutputFormats = ".ass,.srt",
            GeneratedOutputFormats = ".ass,.srt",
            GeneratedSubtitlePaths = JsonSerializer.Serialize(new[] { translatedAssPath, translatedSrtPath }),
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        string? persistedResult = null;
        var settings = new Mock<ISettingService>();
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTag))
            .ReturnsAsync("lingarr");
        settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.SubtitleTagShort))
            .ReturnsAsync("-ai-");
        settings
            .Setup(service => service.SetSetting("subtitle_ass_verification_last_result", It.IsAny<string>()))
            .Callback<string, string>((_, value) => persistedResult = value)
            .ReturnsAsync(true);

        var subtitleService = new SubtitleService(NullLogger<SubtitleService>.Instance);
        var backfillService = new SubtitleOutputBackfillService(
            context,
            subtitleService,
            Mock.Of<ISourceSubtitleSnapshotService>(),
            Mock.Of<ISubtitleExtractionService>(),
            Mock.Of<ISourceSubtitleResolver>(),
            NullLogger<SubtitleOutputBackfillService>.Instance);
        var job = new VerifyAssIntegrityJob(
            subtitleService,
            settings.Object,
            context,
            Mock.Of<IHubContext<JobProgressHub>>(),
            backfillService,
            Mock.Of<ISourceSubtitleResolver>(),
            Mock.Of<ITranslationSubtitleRepairService>(),
            NullLogger<VerifyAssIntegrityJob>.Instance);

        await job.Execute();

        var repairedSrt = await File.ReadAllTextAsync(translatedSrtPath);
        var result = JsonSerializer.Deserialize<AssVerificationResult>(
            persistedResult ?? throw new InvalidOperationException("Verification result was not persisted."),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("Czesc", repairedSrt);
        Assert.DoesNotContain("m 0 0", repairedSrt);
        Assert.Equal(1, result?.LocallyRepairedFiles);
        Assert.DoesNotContain(result!.FlaggedItems, item => item.SubtitlePath == translatedSrtPath);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static string BuildAssContent(string text, string? secondText = null)
    {
        var secondDialogue = secondText == null
            ? string.Empty
            : Environment.NewLine + $"Dialogue: Marked=0,0:00:04.00,0:00:05.00,Sign,NTP,0000,0000,0000,!Effect,{secondText}";

        return $"""
               [Script Info]
               Title: Test
               ScriptType: v4.00+
               WrapStyle: 0

               [V4+ Styles]
               Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
               Style: Sign,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,7,10,10,10,1

               [Events]
               Format: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
               Dialogue: Marked=0,0:00:01.00,0:00:03.00,Sign,NTP,0000,0000,0000,!Effect,{text}{secondDialogue}
               """;
    }

    private static string BuildDamagedSrtContent()
    {
        return $"""
               1
               00:00:01,000 --> 00:00:03,000
               Czesc

               2
               00:00:04,000 --> 00:00:05,000
               m 0 0 l 1 0 1 1 0 1

               3
               00:00:06,000 --> 00:00:07,000
               m 0 0 l 1 0 1 1 0 1

               """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
