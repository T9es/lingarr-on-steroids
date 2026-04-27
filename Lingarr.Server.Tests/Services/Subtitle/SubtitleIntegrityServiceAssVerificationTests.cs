using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleIntegrityServiceAssVerificationTests
{
    [Fact]
    public async Task VerifyAssIntegrityAsync_IncludesUnexpectedAssTagsFromCompletedTranslations()
    {
        await using var context = BuildContext();
        using var tempDirectory = new TemporaryDirectory();
        var sourcePath = Path.Combine(tempDirectory.Path, "movie.en.srt");
        var targetPath = Path.Combine(tempDirectory.Path, "movie.pl.srt");
        await File.WriteAllTextAsync(sourcePath, "1");
        await File.WriteAllTextAsync(targetPath, "1");

        var movie = AddMovie(context, tempDirectory.Path);
        context.TranslationRequests.Add(new TranslationRequest
        {
            WorkloadKind = TranslationWorkloadKind.Library,
            WorkloadItemKey = "library:Movie:1:srt",
            MediaId = movie.Id,
            MediaType = MediaType.Movie,
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "pl",
            SubtitleToTranslate = sourcePath,
            TranslatedSubtitle = targetPath,
            Status = TranslationStatus.Completed,
            CompletedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var subtitleService = new Mock<ISubtitleService>();
        subtitleService
            .Setup(service => service.GetAllSubtitles(tempDirectory.Path))
            .ReturnsAsync([]);
        subtitleService
            .Setup(service => service.ReadSubtitles(sourcePath))
            .ReturnsAsync([Item(1, "Hello")]);
        subtitleService
            .Setup(service => service.ReadSubtitles(targetPath))
            .ReturnsAsync([Item(1, "{\\an7}Czesc")]);
        var sourceSubtitleResolver = new Mock<ISourceSubtitleResolver>();
        sourceSubtitleResolver
            .Setup(service => service.ResolveReadableSourcePathAsync(
                It.IsAny<TranslationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest value, CancellationToken _) => value.SubtitleToTranslate);

        var service = new SubtitleIntegrityService(
            new Mock<ISettingService>().Object,
            subtitleService.Object,
            context,
            sourceSubtitleResolver.Object,
            NullLogger<SubtitleIntegrityService>.Instance);

        var result = await service.VerifyAssIntegrityAsync(CancellationToken.None);

        var item = Assert.Single(result.FlaggedItems);
        Assert.Equal(1, result.TotalFilesScanned);
        Assert.Equal(1, result.FilesWithDrawings);
        Assert.Equal(targetPath, item.SubtitlePath);
        Assert.Contains(AssVerificationIssueTypes.UnexpectedAssTags, item.IssueTypes);
        Assert.Contains("{\\an7}Czesc", item.SuspiciousLines);
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static Movie AddMovie(LingarrDbContext context, string path)
    {
        var movie = new Movie
        {
            RadarrId = 1,
            Title = "Movie",
            FileName = "movie.mkv",
            Path = path,
            DateAdded = DateTime.UtcNow
        };
        context.Movies.Add(movie);
        context.SaveChanges();
        return movie;
    }

    private static SubtitleItem Item(int position, string line)
    {
        return new SubtitleItem
        {
            Position = position,
            Lines = [line],
            PlaintextLines = [line]
        };
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
