using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Services;

public class ProgressService : IProgressService
{
    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly LingarrDbContext _dbContext;

    public ProgressService(
        IHubContext<TranslationRequestsHub> hubContext, 
        LingarrDbContext dbContext)
    {
        _hubContext = hubContext;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task Emit(TranslationRequest translationRequest, int progress)
    {
        // Persist progress to the database using direct update to avoid conflicts
        // with other tracked entities
        await _dbContext.TranslationRequests
            .Where(tr => tr.Id == translationRequest.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(tr => tr.Progress, progress));

        await _hubContext.Clients.Group("TranslationRequests").SendAsync("RequestProgress", new
        {
            Id = translationRequest.Id,
            JobId = translationRequest.JobId,
            CompletedAt = translationRequest.CompletedAt,
            Status = translationRequest.Status.GetDisplayName(),
            Progress = progress
        });
    }

    /// <inheritdoc />
    public async Task EmitBatch(List<TranslationRequest> translationRequests, int progress)
    {
        if (!translationRequests.Any())
        {
            return;
        }

        var ids = translationRequests.Select(tr => tr.Id).ToList();

        // Bulk update Progress in DB
        await _dbContext.TranslationRequests
            .Where(tr => ids.Contains(tr.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(tr => tr.Progress, progress));

        // Throttled SignalR updates
        const int batchSize = 10;
        const int delayMs = 50;

        foreach (var batch in translationRequests.Chunk(batchSize))
        {
            foreach (var request in batch)
            {
                await _hubContext.Clients.Group("TranslationRequests").SendAsync("RequestProgress", new
                {
                    Id = request.Id,
                    JobId = request.JobId,
                    CompletedAt = request.CompletedAt,
                    Status = request.Status.GetDisplayName(),
                    Progress = progress
                });
            }
            await Task.Delay(delayMs);
        }
    }
}
