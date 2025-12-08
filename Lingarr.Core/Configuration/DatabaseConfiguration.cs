using Microsoft.EntityFrameworkCore;

namespace Lingarr.Core.Configuration;

public static class DatabaseConfiguration
{
    public static void ConfigureDbContext(DbContextOptionsBuilder options, string? dbConnection = null)
    {
        dbConnection ??= Environment.GetEnvironmentVariable("DB_CONNECTION")?.ToLower() ?? "sqlite";

        if (dbConnection == "mysql")
        {
            ConfigureMySql(options);
        }
        else
        {
            ConfigureSqlite(options);
        }
    }

    private static void ConfigureMySql(DbContextOptionsBuilder options)
    {
        var variables = new Dictionary<string, string>
        {
            { "DB_HOST", Environment.GetEnvironmentVariable("DB_HOST") ?? "Lingarr.Mysql" },
            { "DB_PORT", Environment.GetEnvironmentVariable("DB_PORT") ?? "3306" },
            { "DB_DATABASE", Environment.GetEnvironmentVariable("DB_DATABASE") ?? "LingarrMysql" },
            { "DB_USERNAME", Environment.GetEnvironmentVariable("DB_USERNAME") ?? "LingarrMysql" },
            { "DB_PASSWORD", Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "Secret1234" }
        };

        var missingVariables = variables.Where(kv => string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList();
        if (missingVariables.Any())
        {
            throw new InvalidOperationException(
                $"MySQL connection environment variable(s) '{string.Join(", ", missingVariables)}' is missing or empty.");
        }

        // Fix Hostname Resolution
        var host = variables["DB_HOST"];
        try 
        {
            var addresses = System.Net.Dns.GetHostAddresses(host);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 != null)
            {
                host = ipv4.ToString();
            }
        }
        catch { /* Ignore DNS errors here, let the connection fail naturally if needed */ }

        var connectionString =
            $"Server={host};Port={variables["DB_PORT"]};Database={variables["DB_DATABASE"]};Uid={variables["DB_USERNAME"]};Pwd={variables["DB_PASSWORD"]};Allow User Variables=True;SslMode=None;Connection Timeout=30";

        // Use static version instead of AutoDetect to avoid blocking connection during startup
        // AutoDetect connects to the database which blocks if MySQL isn't ready
        var serverVersion = new MariaDbServerVersion(new Version(10, 5));
        
        options.UseMySql(connectionString, serverVersion,
                mysqlOptions => mysqlOptions.MigrationsAssembly("Lingarr.Migrations.MySQL")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
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