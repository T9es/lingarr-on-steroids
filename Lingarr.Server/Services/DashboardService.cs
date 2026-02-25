using Hangfire;
using Hangfire.Storage;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

/// <summary>
/// Service for dashboard data aggregation.
/// Uses database persistence for API usage and error logs.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IStatisticsService _statisticsService;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly LingarrDbContext _dbContext;

    public DashboardService(
        IStatisticsService statisticsService,
        ITranslationRequestService translationRequestService,
        LingarrDbContext dbContext)
    {
        _statisticsService = statisticsService;
        _translationRequestService = translationRequestService;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<JobQueueStatus> GetJobQueueStatus()
    {
        var monitoringApi = JobStorage.Current?.GetMonitoringApi();
        
        // Return empty status if Hangfire is not initialized
        if (monitoringApi == null)
        {
            return new JobQueueStatus
            {
                ScheduledCount = 0,
                QueuedCount = 0,
                RunningCount = 0,
                FailedCount = 0,
                SucceededCount = 0,
                Jobs = new List<JobInfo>()
            };
        }
        
        var scheduled = monitoringApi.ScheduledJobs(0, 100);
        var enqueued = monitoringApi.EnqueuedJobs("default", 0, 100);
        var processing = monitoringApi.ProcessingJobs(0, 100);
        var succeeded = monitoringApi.SucceededJobs(0, 10);
        var failed = monitoringApi.FailedJobs(0, 10);

        var jobs = new List<JobInfo>();

        // Add processing jobs
        foreach (var job in processing)
        {
            if (job.Value?.Job?.Method == null) continue;
            
            jobs.Add(new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                State = "Running",
                StartedAt = job.Value.StartedAt,
                Queue = "processing"
            });
        }

        // Add scheduled jobs
        foreach (var job in scheduled)
        {
            if (job.Value?.Job?.Method == null) continue;
            
            jobs.Add(new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                State = "Scheduled",
                ScheduledAt = job.Value.EnqueueAt,
                Queue = "scheduled"
            });
        }

        // Add enqueued jobs
        foreach (var job in enqueued)
        {
            if (job.Value?.Job?.Method == null) continue;
            
            jobs.Add(new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                State = "Queued",
                Queue = "default"
            });
        }

        // Add recent failed jobs
        foreach (var job in failed)
        {
            if (job.Value?.Job?.Method == null) continue;
            
            jobs.Add(new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                State = "Failed",
                FailedAt = job.Value.FailedAt,
                ErrorMessage = job.Value.ExceptionMessage
            });
        }

        // Query translation requests from database (managed by TranslationWorkerService)
        var translationPending = await _dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        var translationInProgress = await _dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.InProgress)
            .OrderByDescending(r => r.StartedAt)
            .Take(20)
            .ToListAsync();

        var translationFailed = await _dbContext.TranslationRequests
            .Where(r => r.Status == TranslationStatus.Failed)
            .OrderByDescending(r => r.FailedAt)
            .Take(10)
            .ToListAsync();

        // Add pending translation requests
        foreach (var request in translationPending)
        {
            jobs.Add(new JobInfo
            {
                Id = $"translation-{request.Id}",
                Name = request.Title,
                State = "Pending",
                Queue = request.IsPriority ? "priority" : "translation",
                ScheduledAt = request.CreatedAt,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage
            });
        }

        // Add in-progress translation requests
        foreach (var request in translationInProgress)
        {
            jobs.Add(new JobInfo
            {
                Id = $"translation-{request.Id}",
                Name = request.Title,
                State = "Running",
                Queue = request.IsPriority ? "priority" : "translation",
                StartedAt = request.StartedAt,
                Progress = request.Progress,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage
            });
        }

        // Add failed translation requests
        foreach (var request in translationFailed)
        {
            jobs.Add(new JobInfo
            {
                Id = $"translation-{request.Id}",
                Name = request.Title,
                State = "Failed",
                Queue = "translation",
                FailedAt = request.FailedAt,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage
            });
        }

        return new JobQueueStatus
        {
            ScheduledCount = scheduled.Count,
            QueuedCount = enqueued.Count + translationPending.Count,
            RunningCount = processing.Count + translationInProgress.Count,
            FailedCount = failed.Count + translationFailed.Count,
            SucceededCount = succeeded.Count,
            Jobs = jobs.OrderByDescending(j => j.StartedAt ?? j.ScheduledAt ?? j.FailedAt).Take(20).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ApiUsageStatus> GetApiUsage()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var recentUsage = await _dbContext.ApiUsageLogs
            .Where(u => u.Timestamp >= cutoff)
            .OrderByDescending(u => u.Timestamp)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var todayUsage = recentUsage.Where(u => u.Timestamp.Date == today);
        var weekUsage = recentUsage;

        var byService = recentUsage
            .GroupBy(u => u.Service)
            .ToDictionary(
                g => g.Key,
                g => new ServiceUsage
                {
                    TotalCalls = g.Count(),
                    TotalTokens = g.Sum(u => u.TokensUsed ?? 0),
                    AverageResponseTime = g.Average(u => u.ResponseTimeMs),
                    ErrorCount = g.Count(u => !u.Success)
                });

        return new ApiUsageStatus
        {
            TotalCallsToday = todayUsage.Count(),
            TotalCallsWeek = weekUsage.Count(),
            TotalTokensToday = todayUsage.Sum(u => u.TokensUsed ?? 0),
            TotalTokensWeek = weekUsage.Sum(u => u.TokensUsed ?? 0),
            AverageResponseTime = weekUsage.Any() ? weekUsage.Average(u => u.ResponseTimeMs) : 0,
            ErrorCount = weekUsage.Count(u => !u.Success),
            ByService = byService,
            RecentCalls = recentUsage.Take(50).Select(u => new ApiUsageEntry
            {
                Timestamp = u.Timestamp,
                Service = u.Service,
                TokensUsed = u.TokensUsed ?? 0,
                ResponseTimeMs = u.ResponseTimeMs,
                Success = u.Success,
                ErrorMessage = u.ErrorMessage
            }).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<List<ErrorLogEntry>> GetErrorLog(int limit = 50)
    {
        var errors = await _dbContext.ErrorLogs
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync();

        return errors.Select(e => new ErrorLogEntry
        {
            Id = e.Id,
            Timestamp = e.Timestamp,
            Source = e.Source,
            Message = e.Message,
            Type = "error",
            Details = e.Details,
            StackTrace = e.StackTrace
        }).ToList();
    }

    /// <inheritdoc />
    public async Task LogApiUsage(string service, int? tokensUsed, long responseTimeMs, bool success, string? errorMessage = null)
    {
        _dbContext.ApiUsageLogs.Add(new ApiUsageLog
        {
            Timestamp = DateTime.UtcNow,
            Service = service,
            TokensUsed = tokensUsed,
            ResponseTimeMs = responseTimeMs,
            Success = success,
            ErrorMessage = errorMessage
        });

        await _dbContext.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task LogError(string source, string message, string? details = null, string? stackTrace = null)
    {
        _dbContext.ErrorLogs.Add(new ErrorLog
        {
            Timestamp = DateTime.UtcNow,
            Source = source,
            Message = message,
            Details = details,
            StackTrace = stackTrace
        });

        await _dbContext.SaveChangesAsync();
    }
}

/// <summary>
/// Interface for dashboard service
/// </summary>
public interface IDashboardService
{
    Task<JobQueueStatus> GetJobQueueStatus();
    Task<ApiUsageStatus> GetApiUsage();
    Task<List<ErrorLogEntry>> GetErrorLog(int limit = 50);
    Task LogApiUsage(string service, int? tokensUsed, long responseTimeMs, bool success, string? errorMessage = null);
    Task LogError(string source, string message, string? details = null, string? stackTrace = null);
}

/// <summary>
/// Job queue status response
/// </summary>
public class JobQueueStatus
{
    public int ScheduledCount { get; set; }
    public int QueuedCount { get; set; }
    public int RunningCount { get; set; }
    public int FailedCount { get; set; }
    public int SucceededCount { get; set; }
    public List<JobInfo> Jobs { get; set; } = new();
}

/// <summary>
/// Individual job information
/// </summary>
public class JobInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int Progress { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
}

/// <summary>
/// API usage status response
/// </summary>
public class ApiUsageStatus
{
    public int TotalCallsToday { get; set; }
    public int TotalCallsWeek { get; set; }
    public int TotalTokensToday { get; set; }
    public int TotalTokensWeek { get; set; }
    public double AverageResponseTime { get; set; }
    public int ErrorCount { get; set; }
    public Dictionary<string, ServiceUsage> ByService { get; set; } = new();
    public List<ApiUsageEntry> RecentCalls { get; set; } = new();
}

/// <summary>
/// Per-service usage statistics
/// </summary>
public class ServiceUsage
{
    public int TotalCalls { get; set; }
    public int TotalTokens { get; set; }
    public double AverageResponseTime { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Individual API usage entry
/// </summary>
public class ApiUsageEntry
{
    public DateTime Timestamp { get; set; }
    public string Service { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public long ResponseTimeMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Error log entry
/// </summary>
public class ErrorLogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "error";
    public string? Details { get; set; }
    public string? StackTrace { get; set; }
}
