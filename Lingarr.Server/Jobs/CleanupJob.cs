using Hangfire;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class CleanupJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<CleanupJob> _logger;
    private readonly IScheduleService _scheduleService;
    private readonly ITranslationDiagnosticsService _translationDiagnosticsService;

    public CleanupJob(
        LingarrDbContext dbContext,
        IScheduleService scheduleService,
        ILogger<CleanupJob> logger,
        ITranslationDiagnosticsService translationDiagnosticsService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _scheduleService = scheduleService;
        _translationDiagnosticsService = translationDiagnosticsService;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

        var oneWeekAgo = DateTime.UtcNow.AddDays(-7);
        var oldJobs = await _dbContext.TranslationRequests
            .Where(pg => pg.CreatedAt < oneWeekAgo)
            .ToListAsync();

        foreach (var job in oldJobs)
        {
            _dbContext.TranslationRequests.Remove(job);
        }

        await _dbContext.SaveChangesAsync();
        var removedDiagnosticCount = await _translationDiagnosticsService.CleanupExpiredAsync(CancellationToken.None);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        _logger.LogInformation(
            "Removed {RequestCount} translation requests and {DiagnosticCount} translation diagnostics that are older than a week.",
            oldJobs.Count,
            removedDiagnosticCount);
    }
}
