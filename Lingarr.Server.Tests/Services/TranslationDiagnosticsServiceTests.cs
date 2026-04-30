using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationDiagnosticsServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public TranslationDiagnosticsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lingarr-diagnostics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task RecordAsync_PersistsDiagnosticEventWithSamples()
    {
        await using var context = CreateDbContext();
        var service = new TranslationDiagnosticsService(
            context,
            NullLogger<TranslationDiagnosticsService>.Instance);

        await service.RecordAsync(new TranslationDiagnosticEventRequest
        {
            TranslationRequestId = 10,
            MediaId = 20,
            MediaType = MediaType.Movie,
            Title = "Cars",
            Stage = "post_write_validation",
            Provider = "gemini",
            SourcePath = "/media/cars.en.srt",
            TargetPath = "/app/config/quarantine/cars.pl.srt",
            OutputFormat = ".srt",
            ReasonCode = SubtitleQualityIssueCodes.UnchangedSourceText,
            Summary = "Target appears to contain source text.",
            SampleLines = ["OK... Here we go. Focus."]
        }, CancellationToken.None);

        var saved = await context.TranslationDiagnosticEvents.SingleAsync();

        Assert.Equal(10, saved.TranslationRequestId);
        Assert.Equal("post_write_validation", saved.Stage);
        Assert.Equal(SubtitleQualityIssueCodes.UnchangedSourceText, saved.ReasonCode);
        Assert.Contains("Here we go", saved.SampleLinesJson);
        Assert.True(saved.ExpiresAt > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesOldEventsAndQuarantineFiles()
    {
        await using var context = CreateDbContext();
        var oldQuarantineFile = Path.Combine(_tempDirectory, "old.srt");
        var freshQuarantineFile = Path.Combine(_tempDirectory, "fresh.srt");
        await File.WriteAllTextAsync(oldQuarantineFile, "old");
        await File.WriteAllTextAsync(freshQuarantineFile, "fresh");

        context.TranslationDiagnosticEvents.AddRange(
            new TranslationDiagnosticEvent
            {
                Stage = "post_write_validation",
                ReasonCode = "old",
                Summary = "old",
                QuarantinePath = oldQuarantineFile,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new TranslationDiagnosticEvent
            {
                Stage = "post_write_validation",
                ReasonCode = "fresh",
                Summary = "fresh",
                QuarantinePath = freshQuarantineFile,
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
        await context.SaveChangesAsync();

        var service = new TranslationDiagnosticsService(
            context,
            NullLogger<TranslationDiagnosticsService>.Instance);

        var removed = await service.CleanupExpiredAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldQuarantineFile));
        Assert.True(File.Exists(freshQuarantineFile));
        Assert.Single(context.TranslationDiagnosticEvents);
    }

    [Fact]
    public void ResolveQuarantineRootPath_UsesWritableContainerConfigPathOnLinux()
    {
        var configuredRoot = Path.Combine(_tempDirectory, "configured");

        Assert.Equal(
            Path.GetFullPath(configuredRoot),
            TranslationDiagnosticsService.ResolveQuarantineRootPath(configuredRoot));

        var defaultRoot = TranslationDiagnosticsService.ResolveQuarantineRootPath(null);
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(
                Path.Combine("config", "translation-quarantine"),
                defaultRoot);
        }
        else
        {
            Assert.Equal("/app/config/translation-quarantine", defaultRoot);
        }
    }

    private static LingarrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LingarrDbContext(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
