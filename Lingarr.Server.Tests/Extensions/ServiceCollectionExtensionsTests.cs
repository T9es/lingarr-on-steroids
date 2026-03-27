using System;
using System.IO;
using System.Reflection;
using Lingarr.Server.Extensions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lingarr.Server.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void EnableSqliteWal_ShouldConfigureWalJournalMode()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hangfire-{Guid.NewGuid():N}.db");

        try
        {
            var method = typeof(ServiceCollectionExtensions).GetMethod(
                "EnableSqliteWal",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            method!.Invoke(null, [databasePath]);

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            var mode = command.ExecuteScalar()?.ToString();

            Assert.Equal("wal", mode);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete($"{databasePath}-wal");
            TryDelete($"{databasePath}-shm");
        }
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
