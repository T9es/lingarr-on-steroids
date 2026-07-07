using System;
using System.IO;
using System.Linq;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lingarr.Server.Tests.Data;

public class TranslationRequestActiveDedupeIndexTests
{
    [Fact]
    public void LingarrDbContext_ActiveDedupeIndex_DoesNotIncludeRequiredOutputFormats()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new LingarrDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(TranslationRequest));
        Assert.NotNull(entityType);

        var dedupeIndex = Assert.Single(
            entityType!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_translation_requests_active_dedupe");

        Assert.Equal(
            new[] { "WorkloadItemKey", "SourceLanguage", "TargetLanguage", "SourceDedupeKey", "IsActive" },
            dedupeIndex.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void PostgreSqlUploadWorkspaceMigration_UsesFormatAgnosticActiveDedupeAndBackfill()
    {
        var source = ReadRepositoryFile(
            "Lingarr.Migrations.PostgreSQL",
            "Migrations",
            "20260419164931_AddUploadWorkspaceTablesAndActiveRequestDedupe.cs");

        Assert.Contains(
            "columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"is_active\" }",
            source);
        Assert.Contains(
            "columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"required_output_formats\", \"is_active\" }",
            source);
        Assert.Contains(
            "PARTITION BY workload_item_key, source_language, target_language",
            source);
        Assert.Contains("SET required_output_formats = CASE", source);
        Assert.Contains("WHEN source_subtitle_format IS NULL", source);
        Assert.Contains("WITH ranked_active AS", source);
    }

    [Fact]
    public void SqliteUploadWorkspaceMigration_UsesFormatAgnosticActiveDedupeAndBackfill()
    {
        var source = ReadRepositoryFile(
            "Lingarr.Migrations.SQLite",
            "Migrations",
            "20260419164916_AddUploadWorkspaceTablesAndActiveRequestDedupe.cs");

        Assert.Contains(
            "columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"is_active\" }",
            source);
        Assert.Contains(
            "columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"required_output_formats\", \"is_active\" }",
            source);
        Assert.Contains(
            "PARTITION BY workload_item_key, source_language, target_language",
            source);
        Assert.Contains("SET required_output_formats = CASE", source);
        Assert.Contains("WHEN source_subtitle_format IS NULL", source);
        Assert.Contains("WITH ranked_active AS", source);
    }

    [Fact]
    public void PostgreSqlSourceDedupeMigration_BackfillsSupplementalRows()
    {
        var source = ReadRepositoryFile(
            "Lingarr.Migrations.PostgreSQL",
            "Migrations",
            "20260429211943_AddSourceDedupeKeyToTranslationRequests.cs");

        Assert.Contains("source_dedupe_key", source);
        Assert.Contains("columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"source_dedupe_key\", \"is_active\" }", source);
        Assert.Contains("UPDATE translation_requests", source);
        Assert.Contains("supplemental:", source);
        Assert.Contains("is_forced_subtitle = TRUE", source);
    }

    [Fact]
    public void SqliteSourceDedupeMigration_BackfillsSupplementalRows()
    {
        var source = ReadRepositoryFile(
            "Lingarr.Migrations.SQLite",
            "Migrations",
            "20260429211929_AddSourceDedupeKeyToTranslationRequests.cs");

        Assert.Contains("source_dedupe_key", source);
        Assert.Contains("columns: new[] { \"workload_item_key\", \"source_language\", \"target_language\", \"source_dedupe_key\", \"is_active\" }", source);
        Assert.Contains("UPDATE translation_requests", source);
        Assert.Contains("supplemental:", source);
        Assert.Contains("is_forced_subtitle = 1", source);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var path = Path.Combine(GetRepositoryRoot(), Path.Combine(parts));
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Lingarr.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Could not locate repository root from test context.");
        }

        return directory.FullName;
    }
}
