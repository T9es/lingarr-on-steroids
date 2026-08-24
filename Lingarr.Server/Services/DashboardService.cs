using Hangfire;
using Hangfire.Storage;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
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
    private readonly ITokenUsageService _tokenUsageService;

    public DashboardService(
        IStatisticsService statisticsService,
        ITranslationRequestService translationRequestService,
        LingarrDbContext dbContext,
        ITokenUsageService tokenUsageService)
    {
        _statisticsService = statisticsService;
        _translationRequestService = translationRequestService;
        _dbContext = dbContext;
        _tokenUsageService = tokenUsageService;
    }

/// <inheritdoc />
    public async Task<JobQueueStatus> GetJobQueueStatus()
    {
        var jobStorage = JobStorage.Current;
        
        if (jobStorage == null)
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
        
        using var connection = jobStorage.GetConnection();
        var monitoringApi = jobStorage.GetMonitoringApi();
        
        var scheduled = monitoringApi.ScheduledJobs(0, 100);
        var enqueued = monitoringApi.EnqueuedJobs("default", 0, 100);
        var processing = monitoringApi.ProcessingJobs(0, 100);
        var succeeded = monitoringApi.SucceededJobs(0, 10);
        var failed = monitoringApi.FailedJobs(0, 10);
        var recurring = connection.GetRecurringJobs();

        var jobs = new List<JobInfo>();

        foreach (var job in processing)
        {
            if (job.Value?.Job?.Method == null) continue;
            
            jobs.Add(new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                JobName = job.Value.Job.Type.Name,
                State = "Running",
                StartedAt = job.Value.StartedAt,
                Queue = "default"
            });
        }

        foreach (var job in recurring)
        {
            var isRunning = job.LastJobId != null && 
                           processing.Any(p => p.Key == job.LastJobId);
            
            if (!isRunning)
            {
                jobs.Add(new JobInfo
                {
                    Id = $"recurring-{job.Id}",
                    Name = job.Job?.Method?.Name ?? job.Id,
                    JobName = job.Id,
                    State = string.IsNullOrEmpty(job.LastJobState) || job.LastJobState == "Succeeded" 
                        ? "Scheduled" 
                        : job.LastJobState,
                    Queue = "recurring",
                    Cron = job.Cron,
                    LastExecution = job.LastExecution,
                    NextExecution = job.NextExecution,
                    ErrorMessage = job.Error
                });
            }
        }

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

        return new JobQueueStatus
        {
            ScheduledCount = recurring.Count,
            QueuedCount = enqueued.Count,
            RunningCount = processing.Count,
            FailedCount = failed.Count,
            SucceededCount = succeeded.Count,
            Jobs = jobs
                .OrderBy(j => j.State == "Running" ? 0 : (j.State == "Scheduled" ? 1 : 2))
                .ThenBy(j => j.NextExecution ?? j.StartedAt ?? j.FailedAt)
                .Take(30)
                .ToList()
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
                g =>
                {
                    var serviceUsage = g.ToList();
                    var today = DateTime.UtcNow.Date;
                    var todayUsage = serviceUsage.Where(u => u.Timestamp.Date == today).ToList();
                    
                    return new ServiceUsage
                    {
                        TotalCalls = serviceUsage.Count(),
                        TotalTokens = serviceUsage.Sum(u => u.TokensUsed ?? 0),
                        AverageResponseTime = serviceUsage.Any() ? serviceUsage.Average(u => u.ResponseTimeMs) : 0,
                        ErrorCount = serviceUsage.Count(u => !u.Success),
                        SuccessRate = serviceUsage.Any() ? (int)((serviceUsage.Count(u => u.Success) / (double)serviceUsage.Count()) * 100) : 100,
                        CallsToday = todayUsage.Count(),
                        CallsWeek = serviceUsage.Count(),
                        CallsMonth = serviceUsage.Count(),
                        
                        DailyBreakdown = serviceUsage
                            .GroupBy(u => u.Timestamp.Date)
                            .OrderBy(d => d.Key)
                            .Select(d => new DailyUsage
                            {
                                Date = d.Key,
                                CallCount = d.Count(),
                                TokenCount = d.Sum(u => u.TokensUsed ?? 0)
                            })
                            .Take(7)
                            .ToList()
                    };
                });

        return new ApiUsageStatus
        {
            TotalCallsToday = todayUsage.Count(),
            TotalCallsWeek = weekUsage.Count(),
            TotalTokensToday = todayUsage.Sum(u => u.TokensUsed ?? 0),
            TotalTokensWeek = weekUsage.Sum(u => u.TokensUsed ?? 0),
            AverageResponseTime = weekUsage.Any() ? weekUsage.Average(u => u.ResponseTimeMs) : 0,
            ErrorCount = weekUsage.Count(u => !u.Success),
            SuccessRate = weekUsage.Any() ? (int)((weekUsage.Count(u => u.Success) / (double)weekUsage.Count()) * 100) : 100,
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
    public async Task<List<ErrorLogEntry>> GetErrorLog(int limit = 50, int offset = 0)
    {
        var errors = await _dbContext.ErrorLogs
            .OrderByDescending(e => e.Timestamp)
            .Skip(Math.Max(offset, 0))
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
    public async Task LogApiUsage(string service, int? tokensUsed, long responseTimeMs, bool success, string? errorMessage = null, int? promptTokens = null, int? completionTokens = null)
    {
        _dbContext.ApiUsageLogs.Add(new ApiUsageLog
        {
            Timestamp = DateTime.UtcNow,
            Service = service,
            TokensUsed = tokensUsed,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            ResponseTimeMs = responseTimeMs,
            Success = success,
            ErrorMessage = errorMessage
        });

        await _dbContext.SaveChangesAsync();

        if (success && completionTokens.GetValueOrDefault() > 0)
        {
            await _tokenUsageService.RecordUsageAsync(service, promptTokens, completionTokens);
        }
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

    /// <inheritdoc />
    public async Task<string?> GetDashboardLayout()
    {
        var setting = await _dbContext.Settings
            .FirstOrDefaultAsync(s => s.Key == SettingKeys.Dashboard.Layout);
        
        return setting?.Value;
    }

    /// <inheritdoc />
    public async Task SaveDashboardLayout(string layoutJson)
    {
        var setting = await _dbContext.Settings
            .FirstOrDefaultAsync(s => s.Key == SettingKeys.Dashboard.Layout);
        
        if (setting == null)
        {
            setting = new Setting
            {
                Key = SettingKeys.Dashboard.Layout,
                Value = layoutJson
            };
            _dbContext.Settings.Add(setting);
        }
        else
        {
            setting.Value = layoutJson;
        }
        
        await _dbContext.SaveChangesAsync();
    }

/// <inheritdoc />
    public async Task ResetDashboardLayout()
    {
        var setting = await _dbContext.Settings
            .FirstOrDefaultAsync(s => s.Key == SettingKeys.Dashboard.Layout);
        
        if (setting != null)
        {
            _dbContext.Settings.Remove(setting);
            await _dbContext.SaveChangesAsync();
        }
    }

/// <inheritdoc />
    public async Task<int> ClearFailedJobs()
    {
        var jobStorage = JobStorage.Current;
        if (jobStorage == null) return 0;
        
        var monitoringApi = jobStorage.GetMonitoringApi();
        var failed = monitoringApi.FailedJobs(0, int.MaxValue);
        var client = new BackgroundJobClient(jobStorage);
        
        foreach (var job in failed)
        {
            client.Delete(job.Key);
        }
        
        return failed.Count;
    }

    /// <inheritdoc />
    public async Task<(List<JobInfo> Jobs, int TotalCount)> GetFailedJobs(int offset = 0, int limit = 10)
    {
        var jobStorage = JobStorage.Current;
        if (jobStorage == null) return (new List<JobInfo>(), 0);
        
        var monitoringApi = jobStorage.GetMonitoringApi();
        var failedCount = monitoringApi.GetStatistics().Failed;
        var failed = monitoringApi.FailedJobs(Math.Max(offset, 0), Math.Max(limit, 0));
        
        var jobs = failed
            .Where(job => job.Value?.Job?.Method != null)
            .Select(job => new JobInfo
            {
                Id = job.Key,
                Name = job.Value.Job.Method.Name,
                JobName = job.Value.Job.Type.Name,
                State = "Failed",
                FailedAt = job.Value.FailedAt,
                ErrorMessage = job.Value.ExceptionMessage
            })
            .ToList();
        
        return (jobs, (int)Math.Min(failedCount, int.MaxValue));
    }
}

/// <summary>
/// Interface for dashboard service
/// </summary>
public interface IDashboardService
{
    Task<JobQueueStatus> GetJobQueueStatus();
    Task<ApiUsageStatus> GetApiUsage();
    Task<List<ErrorLogEntry>> GetErrorLog(int limit = 50, int offset = 0);
    Task LogApiUsage(string service, int? tokensUsed, long responseTimeMs, bool success, string? errorMessage = null, int? promptTokens = null, int? completionTokens = null);
    Task LogError(string source, string message, string? details = null, string? stackTrace = null);
    Task<string?> GetDashboardLayout();
    Task SaveDashboardLayout(string layoutJson);
    Task ResetDashboardLayout();
    Task<int> ClearFailedJobs();
    Task<(List<JobInfo> Jobs, int TotalCount)> GetFailedJobs(int offset = 0, int limit = 10);
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
    public string? JobName { get; set; }
    public string State { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int Progress { get; set; }
    public string? Cron { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? NextExecution { get; set; }
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
