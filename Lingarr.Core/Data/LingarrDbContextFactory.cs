using Lingarr.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lingarr.Core.Data;

public class LingarrDbContextFactory : IDesignTimeDbContextFactory<LingarrDbContext>
{
    public LingarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LingarrDbContext>();
        DatabaseConfiguration.ConfigureDbContext(optionsBuilder);

        return new LingarrDbContext(optionsBuilder.Options);
    }
}

