using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingController : ControllerBase
{
    private readonly ISettingService _settingService;
    private readonly IIntegrationService _integrationService;

    public SettingController(ISettingService settingService, IIntegrationService integrationService)
    {
        _settingService = settingService;
        _integrationService = integrationService;
    }
    
    /// <summary>
    /// Retrieves the value of a specific setting by its key.
    /// </summary>
    /// <param name="key">The key of the setting to retrieve.</param>
    /// <returns>Returns an HTTP 200 OK response with the setting value if found; otherwise,
    /// an HTTP 404 Not Found response.</returns>
    [HttpGet("{key}")]
    public async Task<ActionResult<string?>> GetSetting(string key)
    {
        var value = await _settingService.GetSetting(key);
        if (value != null)
        {
            return Ok(value);
        }

        return BadRequest("Setting not found");
    }

    /// <summary>
    /// Retrieves the values of multiple settings by their keys.
    /// </summary>
    /// <param name="keys">A list of keys for the settings to retrieve.</param>
    /// <returns>Returns an HTTP 200 OK response with a dictionary of setting keys and values.</returns>
    [HttpPost("multiple/get")]
    public async Task<ActionResult<Dictionary<string, string>>> GetSettings([FromBody] IEnumerable<string> keys)
    {
        var settings = await _settingService.GetSettings(keys);
        return Ok(settings);
    }
    
    /// <summary>
    /// Updates or creates a setting with the specified key and value.
    /// </summary>
    /// <param name="setting">The setting object containing the key and value to be updated or created.</param>
    /// <returns>Returns an HTTP 200 OK response if the setting was successfully updated or created; otherwise,
    /// an HTTP 404 Not Found response.</returns>
    [HttpPost]
    public async Task<ActionResult<bool>> SetSetting([FromBody] Setting setting)
    {
        var value = await _settingService.SetSetting(setting.Key, setting.Value);
        if (value)
        {
            return Ok();
        }

        return BadRequest("Setting not found or could not be updated.");
    }

    /// <summary>
    /// Updates or creates multiple settings with the specified keys and values.
    /// </summary>
    /// <param name="settings">A dictionary where the keys are setting keys and the values are the new values to assign.</param>
    /// <returns>Returns an HTTP 200 OK response if all settings were successfully updated or created; otherwise,
    /// an HTTP 400 Bad Request response.</returns>
    [HttpPost("multiple/set")]
    public async Task<ActionResult<bool>> SetSettings([FromBody] Dictionary<string, string> settings)
    {
        var success = await _settingService.SetSettings(settings);
        if (success)
        {
            return Ok();
        }

        return BadRequest("Some settings were not found or could not be updated.");
    }
    
    /// <summary>
    /// Retrieves system configuration limits.
    /// </summary>
    /// <returns>Returns system limits including max concurrent translations.</returns>
    [HttpGet("system/limits")]
    public ActionResult<object> GetSystemLimits()
    {
        var maxConcurrentJobs = int.TryParse(
            Environment.GetEnvironmentVariable("MAX_CONCURRENT_JOBS"), 
            out int value) ? value : 20;
        
        return Ok(new
        {
            MaxConcurrentTranslations = maxConcurrentJobs
        });
    }
    
    /// <summary>
    /// Gets information about translation worker configuration and whether restart is needed.
    /// </summary>
    /// <returns>Returns current configured workers, database setting, and restart status.</returns>
    [HttpGet("system/worker-status")]
    public async Task<ActionResult<object>> GetWorkerStatus()
    {
        // Get current configured worker count (from startup)
        var configuredWorkers = int.TryParse(
            Environment.GetEnvironmentVariable("CONFIGURED_TRANSLATION_WORKERS"),
            out int configured) ? configured : 0;
        
        // Get database setting value
        var dbSetting = await _settingService.GetSetting("max_parallel_translations");
        var dbWorkers = int.TryParse(dbSetting, out int db) ? db : 4;
        
        // Check if restart is needed (db setting differs from configured)
        var restartNeeded = configuredWorkers > 0 && configuredWorkers != dbWorkers;
        
        return Ok(new
        {
            ConfiguredWorkers = configuredWorkers > 0 ? configuredWorkers : dbWorkers,
            DatabaseSetting = dbWorkers,
            RestartNeeded = restartNeeded
        });
    }
    
    /// <summary>
    /// Triggers a graceful restart of the application container.
    /// </summary>
    /// <returns>Returns OK if restart was initiated.</returns>
    [HttpPost("system/restart")]
    public ActionResult Restart()
    {
        // Log the restart request
        Console.WriteLine("[System] Restart requested via API");
        
        // Schedule exit with delay to allow response to be sent
        Task.Run(async () =>
        {
            await Task.Delay(1000);
            Environment.Exit(0);
        });
        
        return Ok(new { Message = "Restart initiated. Container will restart shortly." });
    }
    
    /// <summary>
    /// Tests the connection to the configured Radarr instance.
    /// </summary>
    /// <returns>Returns the connection status and version information.</returns>
    [HttpPost("test/radarr")]
    public async Task<ActionResult<IntegrationTestResult>> TestRadarrConnection()
    {
        var result = await _integrationService.TestConnection(
            new IntegrationSettingKeys
            {
                Url = "radarr_url",
                ApiKey = "radarr_api_key"
            });
        return Ok(result);
    }
    
    /// <summary>
    /// Tests the connection to the configured Sonarr instance.
    /// </summary>
    /// <returns>Returns the connection status and version information.</returns>
    [HttpPost("test/sonarr")]
    public async Task<ActionResult<IntegrationTestResult>> TestSonarrConnection()
    {
        var result = await _integrationService.TestConnection(
            new IntegrationSettingKeys
            {
                Url = "sonarr_url",
                ApiKey = "sonarr_api_key"
            });
        return Ok(result);
    }
}