using Microsoft.EntityFrameworkCore;

namespace Lingarr.Core.Configuration;

public static class DatabaseConfiguration
{
    public static void ConfigureDbContext(DbContextOptionsBuilder options, string? dbConnection = null)
    {
        dbConnection ??= Environment.GetEnvironmentVariable("DB_CONNECTION")?.ToLower() ?? "postgresql";

        if (dbConnection == "sqlite")
        {
            ConfigureSqlite(options);
        }
        else // Default: PostgreSQL
        {
            ConfigurePostgreSQL(options);
        }
    }

    private static void ConfigurePostgreSQL(DbContextOptionsBuilder options)
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "lingarr-postgres";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "lingarr";
        var username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "lingarr";
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "lingarr";

        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};CommandTimeout=120";

        options.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly("Lingarr.Migrations.PostgreSQL")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                    .EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention();
    }

    private static void ConfigureSqlite(DbContextOptionsBuilder options)
    {
        var sqliteDbPath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ?? "local.db";
        options.UseSqlite($"Data Source=/app/config/{sqliteDbPath};Foreign Keys=True",
                sqliteOptions => sqliteOptions.MigrationsAssembly("Lingarr.Migrations.SQLite")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    }
}