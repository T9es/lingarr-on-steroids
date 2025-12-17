using Lingarr.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using EFCore.NamingConventions;

namespace Lingarr.Migrations.PostgreSQL;

public class PostgreSqlDbContextFactory : IDesignTimeDbContextFactory<LingarrDbContext>
{
    public LingarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LingarrDbContext>();
        
        // Use a design-time connection string (requires a running PostgreSQL instance)
        // This is only used for creating migrations, not at runtime
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "lingarr_design";
        var username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "lingarr";
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "lingarr";

        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";

        optionsBuilder.UseNpgsql(connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly("Lingarr.Migrations.PostgreSQL")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    
        return new LingarrDbContext(optionsBuilder.Options);
    }
}
