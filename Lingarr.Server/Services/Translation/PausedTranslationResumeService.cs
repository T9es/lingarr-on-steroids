using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services.Translation;

public class PausedTranslationResumeService : IPausedTranslationResumeService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ITranslationWorkerService _workerService;
    private readonly ILogger<PausedTranslationResumeService> _logger;

    public PausedTranslationResumeService(
        LingarrDbContext dbContext,
        ITranslationWorkerService workerService,
        ILogger<PausedTranslationResumeService> logger)
    {
        _dbContext = dbContext;
        _workerService = workerService;
        _logger = logger;
    }

    public async Task<int> ResumeDuePausedRequestsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var requestIds = await _dbContext.TranslationRequests
            .Where(request => request.Status == TranslationStatus.Paused &&
                              (request.NextRetryAt == null || request.NextRetryAt <= now))
            .Select(request => request.Id)
            .ToListAsync(cancellationToken);

        return await ResumeRequestsAsync(
            requestIds,
            "Paused translation request resumed because its retry window opened.",
            cancellationToken);
    }

    public async Task<int> ResumePausedRequestsForProviderChangeAsync(CancellationToken cancellationToken)
    {
        var requestIds = await _dbContext.TranslationRequests
            .Where(request => request.Status == TranslationStatus.Paused)
            .Select(request => request.Id)
            .ToListAsync(cancellationToken);

        return await ResumeRequestsAsync(
            requestIds,
            "Paused translation request resumed because provider settings changed.",
            cancellationToken);
    }

    private async Task<int> ResumeRequestsAsync(
        List<int> requestIds,
        string logMessage,
        CancellationToken cancellationToken)
    {
        if (requestIds.Count == 0)
        {
            return 0;
        }

        var resumed = await _dbContext.TranslationRequests
            .Where(request => requestIds.Contains(request.Id) && request.Status == TranslationStatus.Paused)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(request => request.Status, TranslationStatus.Pending)
                    .SetProperty(request => request.IsActive, true)
                    .SetProperty(request => request.PausedAt, (DateTime?)null)
                    .SetProperty(request => request.PauseReason, (string?)null)
                    .SetProperty(request => request.PausedProvider, (string?)null)
                    .SetProperty(request => request.NextRetryAt, (DateTime?)null),
                cancellationToken);

        if (resumed == 0)
        {
            return 0;
        }

        foreach (var requestId in requestIds)
        {
            _dbContext.TranslationRequestLogs.Add(new TranslationRequestLog
            {
                TranslationRequestId = requestId,
                Level = "Information",
                Message = logMessage
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _workerService.Signal();

        _logger.LogInformation("Resumed {Count} paused translation request(s)", resumed);
        return resumed;
    }
}
