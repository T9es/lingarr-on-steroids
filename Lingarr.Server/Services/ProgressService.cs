using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;

namespace Lingarr.Server.Services;

/// <summary>
/// Service responsible for emitting translation progress updates to the database and SignalR clients.
/// Uses IServiceScopeFactory to create isolated DbContext instances, avoiding threading conflicts
/// during batch translation where multiple async operations may be in progress.
/// </summary>
public class ProgressService : IProgressService
{
    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;

    public ProgressService(
        IHubContext<TranslationRequestsHub> hubContext, 
        IServiceScopeFactory scopeFactory)
    {
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task Emit(TranslationRequest translationRequest, int progress)
    {
        // Create isolated DbContext to avoid threading conflicts during batch translation
        // The main TranslationJob uses a separate DbContext instance; this prevents
        // "A second operation was started on this context instance" exceptions
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        
        await dbContext.TranslationRequests
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

        // Create isolated DbContext for bulk update
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        
        await dbContext.TranslationRequests
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
