using Microsoft.EntityFrameworkCore;
using Lingarr.Core.Data;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services;

/// <summary>
/// Service that runs on application startup to clean up orphaned Hangfire locks.
/// This prevents job starvation when the application restarts ungracefully (e.g., container crash).
/// </summary>
public class HangfireLockCleanupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireLockCleanupService> _logger;
    private readonly IConfiguration _configuration;

    public HangfireLockCleanupService(
        IServiceProvider serviceProvider,
        ILogger<HangfireLockCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Check if we are using PostgreSQL. SQLite handles locks differently and shouldn't have this table/stiction.
        var dbConnection = _configuration["DB_CONNECTION"]?.ToLower() ?? "postgresql";
        if (dbConnection == "sqlite")
        {
            _logger.LogDebug("Skipping Hangfire lock cleanup for SQLite provider.");
            return;
        }

        try
        {
            // Wait briefly to ensure the database and Hangfire schema are initialized
            await Task.Delay( TimeSpan.FromSeconds(5), cancellationToken);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

            _logger.LogInformation("Wiping orphaned Hangfire distributed locks from database on startup...");

            // Execute raw SQL to clear the lock table.
            // Since the application has just started, any locks existing in the table are by definition
            // orphans from a previous, ungracefully terminated instance.
            var deletedCount = await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM hangfire.lock", cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Successfully cleared {Count} orphaned Hangfire distributed locks.", deletedCount);
            }
            else
            {
                _logger.LogInformation("No orphaned Hangfire distributed locks found to clear.");
            }
        }
        catch (Exception ex)
        {
            // We don't want to crash the whole application if this cleanup fail, 
            // but we absolutely should log it as a warning.
            _logger.LogWarning(ex, "An error occurred while cleaning up Hangfire distributed locks on startup.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
