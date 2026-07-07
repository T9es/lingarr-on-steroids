using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Jobs;

public class UploadWorkspaceCleanupJob
{
    private readonly IUploadWorkspaceCleanupService _uploadWorkspaceCleanupService;
    private readonly IScheduleService _scheduleService;
    private readonly ILogger<UploadWorkspaceCleanupJob> _logger;

    public UploadWorkspaceCleanupJob(
        IUploadWorkspaceCleanupService uploadWorkspaceCleanupService,
        IScheduleService scheduleService,
        ILogger<UploadWorkspaceCleanupJob> logger)
    {
        _uploadWorkspaceCleanupService = uploadWorkspaceCleanupService;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("system")]
    public async Task Execute()
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());

        var expiredBatchCount = await _uploadWorkspaceCleanupService.CleanupExpiredBatchesAsync();
        var expiredArtifactCount = await _uploadWorkspaceCleanupService.CleanupExpiredArtifactsAsync();
        var staleIntermediateCount = await _uploadWorkspaceCleanupService.CleanupStaleIntermediatesAsync();

        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
        _logger.LogInformation(
            "Upload workspace cleanup complete. Expired batches: {ExpiredBatches}, expired artifacts: {ExpiredArtifacts}, stale intermediates: {StaleIntermediates}.",
            expiredBatchCount,
            expiredArtifactCount,
            staleIntermediateCount);
    }
}
