using Lingarr.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using EFCore.NamingConventions;

namespace Lingarr.Migrations.SQLite;

public class SqliteDbContextFactory : IDesignTimeDbContextFactory<LingarrDbContext>
{
    public LingarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LingarrDbContext>();
        
        // Use a local temp database for design-time migrations
        optionsBuilder.UseSqlite("Data Source=design_time.db;Foreign Keys=True",
                sqliteOptions => sqliteOptions.MigrationsAssembly("Lingarr.Migrations.SQLite")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    
        return new LingarrDbContext(optionsBuilder.Options);
    }
}

