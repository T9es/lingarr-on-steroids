using System.Net.Http;
using GTranslate.Translators;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Translation;

public class TranslationFactory : ITranslationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationFactory> _logger;
    private readonly ITokenUsageService? _tokenUsageService;

    public TranslationFactory(IServiceProvider serviceProvider,
        ILogger<TranslationFactory> logger,
        ITokenUsageService? tokenUsageService = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tokenUsageService = tokenUsageService;
    }

    /// <inheritdoc />
    public ITranslationService CreateTranslationService(string serviceType)
    {
        return serviceType.ToLower() switch
        {
            "libretranslate" => new LibreService(
                _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<LibreService>>()),

            "google" => new GTranslatorService<GoogleTranslator>(
                _serviceProvider.GetRequiredService<GoogleTranslator>(),
                "/app/Statics/google_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<GoogleTranslator>>()
            ),

            "bing" => new GTranslatorService<BingTranslator>(
                _serviceProvider.GetRequiredService<BingTranslator>(),
                "/app/Statics/bing_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<BingTranslator>>()
            ),

            "microsoft" => new GTranslatorService<MicrosoftTranslator>(
                _serviceProvider.GetRequiredService<MicrosoftTranslator>(),
                "/app/Statics/microsoft_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<MicrosoftTranslator>>()
            ),

            "yandex" => new GTranslatorService<YandexTranslator>(
                _serviceProvider.GetRequiredService<YandexTranslator>(),
                "/app/Statics/yandex_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<YandexTranslator>>()
            ),

            "deepl" => new DeepLService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<DeepLService>>()
            ),

            "openai" => new OpenAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<OpenAiService>>(),
                _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAiService)),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "anthropic" => new AnthropicService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<AnthropicService>>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "localai" => new LocalAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<LocalAiService>>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "deepseek" => new DeepSeekService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<DeepSeekService>>(),
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "gemini" => new GoogleGeminiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<GoogleGeminiService>>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "chutes" => new ChutesAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<ChutesAiService>>(),
                _serviceProvider.GetRequiredService<IChutesUsageService>(),
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            "nanogpt" => new NanoGptService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<NanoGptService>>(),
                _serviceProvider.GetRequiredService<INanoGptUsageService>(),
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                _serviceProvider.GetRequiredService<IMemoryCache>(),
                _serviceProvider.GetService<IDashboardService>(),
                _tokenUsageService
            ),

            _ => throw new ArgumentException("Unsupported translation service type", nameof(serviceType))
        };
    }
}
