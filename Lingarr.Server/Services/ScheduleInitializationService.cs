using Hangfire;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Jobs;

namespace Lingarr.Server.Services;

public class ScheduleInitializationService : IHostedService
{
    private readonly IScheduleService _scheduleService;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduleInitializationService> _logger;

    public ScheduleInitializationService(
        IScheduleService scheduleService,
        IHostApplicationLifetime appLifetime,
        IServiceProvider serviceProvider,
        ILogger<ScheduleInitializationService> logger)
    {
        _scheduleService = scheduleService;
        _appLifetime = appLifetime;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        return Task.CompletedTask;
    }
    
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// This method is called when the application starts and performs the initialization of the <see cref="_scheduleService"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="Exception">Logs any exception that occurs during the initialization of the <see cref="_scheduleService"/>.</exception>
    private async void OnApplicationStarted()
    {
        try
        {
            _logger.LogInformation("Initializing Schedule Service...");
            await _scheduleService.Initialize();
            _logger.LogInformation("Schedule Service initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing ScheduleService.");
        }

        ScheduleStartupImportRecovery();
    }

    /// <summary>
    /// Queues a one-shot Sonarr import recovery after the application has started, so the
    /// Hangfire server is up. Restarting the application can drop download webhooks for
    /// episodes that Sonarr imports while the app is down; without this pass those episodes
    /// stay invisible until the next full show sync.
    /// </summary>
    private void ScheduleStartupImportRecovery()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetService<IBackgroundJobClient>();
            if (backgroundJobClient == null)
            {
                _logger.LogWarning("Hangfire client is not available; skipping Sonarr import recovery");
                return;
            }

            backgroundJobClient.Schedule<StartupImportRecoveryJob>(
                job => job.Execute(),
                TimeSpan.FromMinutes(1));
            _logger.LogInformation("Scheduled Sonarr import recovery to run one minute after startup");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to schedule Sonarr import recovery");
        }
    }
}