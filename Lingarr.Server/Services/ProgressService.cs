using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Hubs;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;
using System.Collections.Concurrent;

namespace Lingarr.Server.Services;

/// <summary>
/// Service responsible for emitting translation progress updates to the database and SignalR clients.
/// Uses IServiceScopeFactory to create isolated DbContext instances, avoiding threading conflicts
/// during batch translation where multiple async operations may be in progress.
/// </summary>
public class ProgressService : IProgressService
{
    private static readonly TimeSpan EmitThrottleWindow = TimeSpan.FromMilliseconds(250);

    private readonly IHubContext<TranslationRequestsHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, DateTime> _lastIntermediateEmitByRequestId = new();

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
        var isTerminal = IsTerminalProgress(translationRequest, progress);
        if (!isTerminal && !ShouldEmitIntermediateProgress(translationRequest.Id, progress))
        {
            return;
        }

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
            Title = translationRequest.Title,
            MediaType = translationRequest.MediaType.ToString(),
            SourceLanguage = translationRequest.SourceLanguage,
            TargetLanguage = translationRequest.TargetLanguage,
            StartedAt = translationRequest.StartedAt,
            CompletedAt = translationRequest.CompletedAt,
            Status = translationRequest.Status.GetDisplayName(),
            Progress = progress
        });

        if (isTerminal)
        {
            _lastIntermediateEmitByRequestId.TryRemove(translationRequest.Id, out _);
        }
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
                    Title = request.Title,
                    MediaType = request.MediaType.ToString(),
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    StartedAt = request.StartedAt,
                    CompletedAt = request.CompletedAt,
                    Status = request.Status.GetDisplayName(),
                    Progress = progress
                });
            }
            await Task.Delay(delayMs);
        }
    }

    private bool ShouldEmitIntermediateProgress(int requestId, int progress)
    {
        if (progress == 0 || progress == 100)
        {
            _lastIntermediateEmitByRequestId[requestId] = DateTime.UtcNow;
            return true;
        }

        var now = DateTime.UtcNow;
        if (!_lastIntermediateEmitByRequestId.TryGetValue(requestId, out var lastEmit))
        {
            _lastIntermediateEmitByRequestId[requestId] = now;
            return true;
        }

        if (now - lastEmit < EmitThrottleWindow)
        {
            return false;
        }

        _lastIntermediateEmitByRequestId[requestId] = now;
        return true;
    }

    private static bool IsTerminalProgress(TranslationRequest translationRequest, int progress)
    {
        return progress == 0 ||
               progress == 100 ||
               translationRequest.Status is
                   Lingarr.Core.Enum.TranslationStatus.Completed or
                   Lingarr.Core.Enum.TranslationStatus.Failed or
                   Lingarr.Core.Enum.TranslationStatus.Cancelled or
                   Lingarr.Core.Enum.TranslationStatus.Interrupted;
    }
}
