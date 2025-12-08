using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Controllers;

/// <summary>
/// Controller for test translations with real-time logging.
/// Test translations do NOT save the result - they are for debugging only.
/// </summary>
[ApiController]
[Route("api/test-translation")]
public class TestTranslationController : ControllerBase
{
    private readonly ITestTranslationService _testTranslationService;
    private readonly ILogger<TestTranslationController> _logger;
    
    public TestTranslationController(
        ITestTranslationService testTranslationService,
        ILogger<TestTranslationController> logger)
    {
        _testTranslationService = testTranslationService;
        _logger = logger;
    }
    
    /// <summary>
    /// Get current test status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<object> GetStatus()
    {
        return Ok(new { IsRunning = _testTranslationService.IsRunning });
    }
    
    /// <summary>
    /// Start a test translation with real-time log streaming via SSE.
    /// </summary>
    [HttpPost("start")]
    public async Task StartTest([FromBody] TestTranslationRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        
        var completionSource = new TaskCompletionSource<TestTranslationResult>();
        
        async void OnLogEntry(object? sender, TestTranslationLogEntry entry)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    type = "log",
                    entry.Level,
                    entry.Message,
                    entry.Timestamp,
                    entry.Details
                });
                
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to write log entry to SSE stream: {Error}", ex.Message);
            }
        }
        
        _testTranslationService.OnLogEntry += OnLogEntry;
        
        try
        {
            var result = await _testTranslationService.RunTestAsync(request, cancellationToken);
            
            // Send final result
            var resultJson = JsonSerializer.Serialize(new
            {
                type = "result",
                result.Success,
                result.ErrorMessage,
                result.TotalSubtitles,
                result.TranslatedCount,
                Duration = result.Duration.TotalSeconds,
                result.Preview
            });
            
            await Response.WriteAsync($"data: {resultJson}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _testTranslationService.OnLogEntry -= OnLogEntry;
        }
    }
    
    /// <summary>
    /// Cancel any in-progress test translation.
    /// </summary>
    [HttpPost("cancel")]
    public ActionResult Cancel()
    {
        _testTranslationService.CancelTest();
        return Ok(new { Message = "Cancellation requested" });
    }
}
