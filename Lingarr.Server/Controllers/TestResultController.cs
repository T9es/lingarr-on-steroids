using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/test-results")]
public class TestResultController : ControllerBase
{
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<TestResultController> _logger;

    public TestResultController(
        LingarrDbContext dbContext,
        ILogger<TestResultController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetHistory(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        
        var total = await _dbContext.TestResults.CountAsync(cancellationToken);
        
        var results = await _dbContext.TestResults
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.SubtitlePath,
                r.Title,
                r.PosterPath,
                r.SourceLanguage,
                r.TargetLanguage,
                r.Success,
                r.ErrorMessage,
                r.TotalLines,
                r.TranslatedLines,
                r.FailedLines,
                r.DurationSeconds,
                r.TranslationService,
                r.CreatedAt
            })
            .ToListAsync(cancellationToken);
        
        return Ok(new
        {
            items = results,
            totalCount = total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TestResult>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.TestResults
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        
        if (result == null)
        {
            return NotFound();
        }
        
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.TestResults
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        
        if (result == null)
        {
            return NotFound();
        }
        
        _dbContext.TestResults.Remove(result);
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        return Ok(new { Message = "Test result deleted" });
    }

    [HttpDelete]
    public async Task<ActionResult> DeleteMultiple([FromBody] int[] ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Length == 0)
        {
            return BadRequest(new { Message = "No IDs provided" });
        }
        
        var results = await _dbContext.TestResults
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);
        
        if (results.Count == 0)
        {
            return NotFound();
        }
        
        _dbContext.TestResults.RemoveRange(results);
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        return Ok(new { Message = $"Deleted {results.Count} test results" });
    }

    [HttpDelete("all")]
    public async Task<ActionResult> DeleteAll(CancellationToken cancellationToken = default)
    {
        var results = await _dbContext.TestResults.ToListAsync(cancellationToken);

        if (results.Count == 0)
        {
            return NotFound();
        }

        _dbContext.TestResults.RemoveRange(results);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { Message = $"Deleted {results.Count} test results" });
    }

    [HttpGet("check-duplicate")]
    public async Task<ActionResult<object?>> CheckDuplicate(
        string subtitlePath,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.TestResults
            .Where(r => r.SubtitlePath == subtitlePath &&
                       r.SourceLanguage == sourceLanguage &&
                       r.TargetLanguage == targetLanguage)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Success,
                r.CreatedAt,
                r.DurationSeconds
            })
            .FirstOrDefaultAsync(cancellationToken);
        
        return Ok(existing);
    }
}
