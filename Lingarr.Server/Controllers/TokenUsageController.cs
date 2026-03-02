using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TokenUsageController : ControllerBase
{
    private readonly ITokenUsageService _tokenUsageService;
    private readonly ISettingService _settings;

    public TokenUsageController(
        ITokenUsageService tokenUsageService,
        ISettingService settings)
    {
        _tokenUsageService = tokenUsageService;
        _settings = settings;
    }

    [HttpGet("{service}")]
    public async Task<ActionResult<TokenUsageResponse>> GetUsage(string service)
    {
        var usage = await _tokenUsageService.GetUsageAsync(service);
        
        // Get reset time setting for display
        var resetTimeSetting = await _settings.GetSetting(SettingKeys.Translation.TokenLimits.TokenLimitResetTime);
        
        return Ok(new TokenUsageResponse
        {
            Service = usage.Service,
            TokensUsedToday = usage.TokensUsedToday,
            TokenLimit = usage.TokenLimit,
            ResetAt = usage.ResetAt,
            LimitEnabled = usage.LimitEnabled,
            PercentUsed = usage.PercentUsed,
            ResetTimeSetting = resetTimeSetting ?? "00:00"
        });
    }

    [HttpGet("chutes-mode")]
    public async Task<ActionResult<ChutesModeResponse>> GetChutesMode()
    {
        var mode = await _settings.GetSetting(SettingKeys.Translation.TokenLimits.ChutesMode);
        return Ok(new ChutesModeResponse { Mode = mode ?? "subscription" });
    }

    [HttpPut("chutes-mode")]
    public async Task<IActionResult> SetChutesMode([FromBody] ChutesModeRequest request)
    {
        if (request.Mode != "subscription" && request.Mode != "payg")
        {
            return BadRequest("Mode must be 'subscription' or 'payg'");
        }

        await _settings.SetSetting(SettingKeys.Translation.TokenLimits.ChutesMode, request.Mode);
        return Ok();
    }
}

public class TokenUsageResponse
{
    public string Service { get; set; } = string.Empty;
    public long TokensUsedToday { get; set; }
    public long? TokenLimit { get; set; }
    public DateTime? ResetAt { get; set; }
    public bool LimitEnabled { get; set; }
    public double PercentUsed { get; set; }
    public string ResetTimeSetting { get; set; } = "00:00";
}

public class ChutesModeResponse
{
    public string Mode { get; set; } = "subscription";
}

public class ChutesModeRequest
{
    public string Mode { get; set; } = "subscription";
}
