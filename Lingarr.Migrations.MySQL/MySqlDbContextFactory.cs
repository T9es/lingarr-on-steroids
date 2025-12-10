using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Lingarr.Core.Data;
using EFCore.NamingConventions;

namespace Lingarr.Migrations.MySQL;

public class MySqlDbContextFactory : IDesignTimeDbContextFactory<LingarrDbContext>
{
    public LingarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LingarrDbContext>();
        
        // Use a dummy connection string with a fixed server version for design-time migrations
        // This avoids needing a live MySQL connection during migration generation
        var dummyConnectionString = "Server=localhost;Database=lingarr;Uid=root;Pwd=password;";
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        
        optionsBuilder.UseMySql(dummyConnectionString, serverVersion,
                mysqlOptions => mysqlOptions.MigrationsAssembly("Lingarr.Migrations.MySQL")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
        
        return new LingarrDbContext(optionsBuilder.Options);
    }
}
