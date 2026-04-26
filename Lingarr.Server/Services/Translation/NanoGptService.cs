using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

public class NanoGptService : OpenAiService
{
    protected override string ModelSettingKey => SettingKeys.Translation.NanoGpt.Model;
    protected override string ApiKeySettingKey => SettingKeys.Translation.NanoGpt.ApiKey;
    protected override string EndpointBase => "https://nano-gpt.com/api/v1/";
    protected override string ServiceName => "nanogpt";

    public NanoGptService(
        ISettingService settings,
        ILogger<NanoGptService> logger,
        IHttpClientFactory httpClientFactory,
        IDashboardService? dashboardService = null,
        ITokenUsageService? tokenUsageService = null)
        : base(settings, logger, httpClientFactory.CreateClient(nameof(NanoGptService)), dashboardService, tokenUsageService)
    {
    }
}
