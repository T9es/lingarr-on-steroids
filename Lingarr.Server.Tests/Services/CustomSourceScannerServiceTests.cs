using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class CustomSourceScannerServiceTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), $"lingarr-custom-{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanSourceAsync_IndexesMovieFilesAndRemovesMissingItemsWithoutTouchingLibraryMedia()
    {
        Directory.CreateDirectory(_rootDirectory);

        var firstMoviePath = Path.Combine(_rootDirectory, "Movie One (2024).mkv");
        var secondMoviePath = Path.Combine(_rootDirectory, "Movie Two.MP4");
        var ignoredPath = Path.Combine(_rootDirectory, "notes.txt");

        await File.WriteAllTextAsync(firstMoviePath, "movie-1");
        await File.WriteAllTextAsync(secondMoviePath, "movie-2");
        await File.WriteAllTextAsync(ignoredPath, "ignore-me");

        await using var context = BuildContext();

        var customSource = new CustomSource
        {
            Id = 1,
            Name = "Movies",
            SourceType = CustomSourceType.MovieRoot,
            RootPath = _rootDirectory,
            Recursive = true,
            Enabled = true,
            IncludeInAutomation = true
        };

        var libraryMovie = new Movie
        {
            Id = 99,
            RadarrId = 99,
            Title = "Arr Movie",
            FileName = "arr.mkv",
            Path = "/movies",
            DateAdded = DateTime.UtcNow
        };

        context.CustomSources.Add(customSource);
        context.Movies.Add(libraryMovie);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var firstScan = await service.ScanSourceAsync(customSource.Id);

        Assert.Equal(2, firstScan.IndexedCount);
        Assert.Equal(0, firstScan.RemovedCount);

        var indexedItems = await context.CustomMediaItems
            .Where(item => item.CustomSourceId == customSource.Id)
            .OrderBy(item => item.FileName)
            .ToListAsync();

        Assert.Equal(2, indexedItems.Count);
        Assert.All(indexedItems, item => Assert.Equal(CustomMediaItemKind.Movie, item.ItemKind));
        Assert.Contains(indexedItems, item => item.Path == firstMoviePath);
        Assert.Contains(indexedItems, item => item.Path == secondMoviePath);
        Assert.DoesNotContain(indexedItems, item => item.Path == ignoredPath);

        File.Delete(secondMoviePath);

        var secondScan = await service.ScanSourceAsync(customSource.Id);

        Assert.Equal(1, secondScan.IndexedCount);
        Assert.Equal(1, secondScan.RemovedCount);
        Assert.Single(await context.CustomMediaItems.Where(item => item.CustomSourceId == customSource.Id).ToListAsync());
        Assert.NotNull(await context.Movies.FindAsync(libraryMovie.Id));
    }

    private static LingarrDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LingarrDbContext(options);
    }

    private static CustomSourceScannerService CreateService(LingarrDbContext context)
    {
        return new CustomSourceScannerService(
            context,
            new DirectoryService(),
            Mock.Of<ISettingService>(),
            NullLogger<CustomSourceScannerService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
