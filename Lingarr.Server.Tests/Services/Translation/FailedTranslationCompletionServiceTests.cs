using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class FailedTranslationCompletionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _dbContext;
    private readonly string _tempDirectory;

    public FailedTranslationCompletionServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new LingarrDbContext(options);
        _dbContext.Database.EnsureCreated();

        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CompleteAsync_WhenAssRequestRequiresAssAndSrt_WritesAndRecordsBothOutputs()
    {
        var sourcePath = Path.Combine(_tempDirectory, "episode.en.ass");
        await File.WriteAllTextAsync(sourcePath, CreateAssSubtitle());

        var request = new TranslationRequest
        {
            Title = "Episode",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            SourceSubtitleFormat = ".ass",
            SubtitleOutputMode = "both",
            RequiredOutputFormats = ".ass,.srt",
            MediaType = MediaType.Episode,
            Status = TranslationStatus.Failed,
            WorkloadKind = TranslationWorkloadKind.CustomSource
        };

        _dbContext.TranslationRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        var checkpoint = new TranslationCheckpoint
        {
            TranslationRequestId = request.Id,
            SourceFingerprint = "source",
            Translations =
            {
                [1] = "Przetlumaczony tekst"
            }
        };

        var checkpointService = new Mock<ITranslationCheckpointService>();
        checkpointService
            .Setup(service => service.LoadByRequestIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkpoint);
        checkpointService
            .Setup(service => service.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sourcePath,
            checkpointService.Object,
            new Dictionary<string, string>
            {
                [SettingKeys.Translation.UseSubtitleTagging] = "true",
                [SettingKeys.Translation.RemoveLanguageTag] = "false",
                [SettingKeys.Translation.SubtitleTag] = "-ai-",
                [SettingKeys.Translation.SubtitleTagShort] = "-ai-",
                [SettingKeys.Translation.StripSubtitleFormatting] = "false",
                [SettingKeys.Translation.SubtitleOutputMode] = "both"
            });

        var result = await service.CompleteAsync(
            request,
            new Dictionary<int, string>(),
            new HashSet<int> { 3 },
            "Auto-completed test request.",
            CancellationToken.None);

        Assert.True(result.Completed);
        var updatedRequest = await _dbContext.TranslationRequests.FindAsync(request.Id);
        Assert.NotNull(updatedRequest);
        Assert.Equal(TranslationStatus.Completed, updatedRequest!.Status);
        Assert.Equal(".ass,.srt", updatedRequest.GeneratedOutputFormats);
        Assert.Equal(".ass,.srt", updatedRequest.RequiredOutputFormats);

        var generatedPaths = JsonSerializer.Deserialize<List<string>>(updatedRequest.GeneratedSubtitlePaths!);
        Assert.NotNull(generatedPaths);
        Assert.Equal(2, generatedPaths!.Count);
        Assert.Contains(generatedPaths, path => Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(generatedPaths, path => Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase));
        Assert.All(generatedPaths, path => Assert.True(File.Exists(path), $"Expected output file to exist: {path}"));

        var srtPath = generatedPaths.Single(path =>
            Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase));
        var srtContent = await File.ReadAllTextAsync(srtPath);

        Assert.Contains("Przetlumaczony tekst", srtContent);
        Assert.Contains("Echo source text", srtContent);
        Assert.DoesNotContain(@"\p1", srtContent);
        Assert.DoesNotContain("m 0 0 l 10 10", srtContent);
        Assert.Equal(updatedRequest.TranslatedSubtitle, result.OutputPath);
        checkpointService.Verify(
            service => service.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private FailedTranslationCompletionService CreateService(
        string sourcePath,
        ITranslationCheckpointService checkpointService,
        Dictionary<string, string> settings)
    {
        var sourceResolver = new Mock<ISourceSubtitleResolver>();
        sourceResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePath);

        var settingService = new Mock<ISettingService>();
        settingService
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys
                .Where(settings.ContainsKey)
                .ToDictionary(key => key, key => settings[key]));

        var requestService = new Mock<ITranslationRequestService>();
        requestService
            .Setup(service => service.UpdateActiveCount())
            .ReturnsAsync(0);

        var progressService = new Mock<IProgressService>();
        progressService
            .Setup(service => service.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        return new FailedTranslationCompletionService(
            _dbContext,
            sourceResolver.Object,
            new SubtitleService(NullLogger<SubtitleService>.Instance),
            checkpointService,
            settingService.Object,
            requestService.Object,
            progressService.Object,
            Mock.Of<IMediaStateService>(),
            NullLogger<FailedTranslationCompletionService>.Instance);
    }

    private static string CreateAssSubtitle()
    {
        return "[Script Info]\n" +
               "ScriptType: v4.00+\n\n" +
               "[V4+ Styles]\n" +
               "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
               "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1\n\n" +
               "[Events]\n" +
               "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
               "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello\n" +
               "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,{\\p1}m 0 0 l 10 10{\\p0}\n" +
               "Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,Echo source text\n";
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Close();
        _connection.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
