using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

public class PausedTranslationMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PausedTranslationMonitorService> _logger;

    public PausedTranslationMonitorService(
        IServiceProvider serviceProvider,
        ILogger<PausedTranslationMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var resumeService = scope.ServiceProvider.GetRequiredService<IPausedTranslationResumeService>();
                await resumeService.ResumeDuePausedRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resume due paused translation requests");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
