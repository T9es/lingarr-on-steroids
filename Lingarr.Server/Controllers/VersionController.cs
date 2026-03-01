using Microsoft.AspNetCore.Mvc;
using Lingarr.Core;
using Lingarr.Core.Models;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    /// <summary>
    /// Retrieves the current version information and checks for available updates.
    /// </summary>
    /// <returns>A Task containing an ActionResult with VersionInfo data. Returns HTTP 200 OK on success.</returns>
    /// <response code="200">Returns the version information</response>
    /// <response code="500">If there was an error checking for updates</response>
    [HttpGet]
    public async Task<ActionResult<VersionInfo>> Get()
    {
        var versionInfo = await LingarrVersion.CheckForUpdates();
        return Ok(versionInfo);
    }

    /// <summary>
    /// Retrieves the localized README content.
    /// </summary>
    /// <param name="lang">The language code (e.g., "en", "nl", "de"). Defaults to "en" if not specified.</param>
    /// <returns>A string containing the README markdown content. Returns HTTP 200 OK on success.</returns>
    /// <response code="200">Returns the README content</response>
    /// <response code="404">If the README file was not found</response>
    [HttpGet("readme")]
    public ActionResult<string> GetReadme([FromQuery] string? lang)
    {
        var readmeDir = Path.Combine(AppContext.BaseDirectory, "Readmes");
        
        if (string.IsNullOrEmpty(lang))
        {
            lang = "en";
        }

        var fileName = lang.Equals("en", StringComparison.OrdinalIgnoreCase) 
            ? "Readme.MD" 
            : $"Readme.{lang}.md";
        
        var filePath = Path.Combine(readmeDir, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            filePath = Path.Combine(readmeDir, "Readme.MD");
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("README file not found");
        }

        var content = System.IO.File.ReadAllText(filePath);
        return Ok(content);
    }
}