// Service Pattern Example - TranslationFactory.cs
// Demonstrates Factory Pattern with dependency injection

using System.Net.Http;
using GTranslate.Translators;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation;

public class TranslationFactory : ITranslationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationFactory> _logger;

    public TranslationFactory(IServiceProvider serviceProvider,
        ILogger<TranslationFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

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

            "deepl" => new DeepLService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<DeepLService>>()
            ),

            "openai" => new OpenAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<OpenAiService>>()
            ),

            "anthropic" => new AnthropicService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<AnthropicService>>()
            ),

            _ => throw new ArgumentException("Unsupported translation service type", nameof(serviceType))
        };
    }
}
