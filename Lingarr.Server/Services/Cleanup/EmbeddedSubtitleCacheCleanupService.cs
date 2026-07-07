using Lingarr.Server.Interfaces.Services.Subtitle;

namespace Lingarr.Server.Services.Cleanup;

public class EmbeddedSubtitleCacheCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IEmbeddedSubtitleCacheService _cacheService;
    private readonly ILogger<EmbeddedSubtitleCacheCleanupService> _logger;

    public EmbeddedSubtitleCacheCleanupService(
        IEmbeddedSubtitleCacheService cacheService,
        ILogger<EmbeddedSubtitleCacheCleanupService> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cacheService.CleanupExpiredFilesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedded subtitle cache cleanup failed");
        }
    }
}
