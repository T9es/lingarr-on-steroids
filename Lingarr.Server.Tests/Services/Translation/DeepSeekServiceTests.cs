using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Tests.Data;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class DeepSeekServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_DoesNotForceDefaultMaxTokensWhenCustomParametersAreBlank()
    {
        var handler = new CapturingDeepSeekHandler();
        var service = new DeepSeekService(
            CreateSettingService(),
            new Mock<ILogger<DeepSeekService>>().Object,
            new StaticHttpClientFactory(new HttpClient(handler)));

        var result = await service.TranslateBatchAsync(
            [new BatchSubtitleItem { Position = 1, Line = "Hello there" }],
            "en",
            "pl",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Czesc tam", result[1]);
        var body = Assert.Single(handler.ChatBodies);
        using var json = JsonDocument.Parse(body);

        Assert.False(json.RootElement.TryGetProperty("max_tokens", out _));
    }

    private static ISettingService CreateSettingService()
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.DeepSeek.ApiKey, "test-api-key" },
            { SettingKeys.Translation.DeepSeek.Model, "deepseek-v4-flash" },
            { SettingKeys.Translation.AiPrompt, "Translate from {sourceLanguage} to {targetLanguage}." },
            { SettingKeys.Translation.AiContextPrompt, "Context." },
            { SettingKeys.Translation.AiContextPromptEnabled, "false" },
            { SettingKeys.Translation.CustomAiParameters, "[]" },
            { SettingKeys.Translation.RequestTimeout, "30" },
            { SettingKeys.Translation.MaxRetries, "1" },
            { SettingKeys.Translation.RetryDelay, "1" },
            { SettingKeys.Translation.RetryDelayMultiplier, "2" }
        };

        var settingsMock = new Mock<ISettingService>();
        settingsMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys.ToDictionary(
                key => key,
                key => settings.TryGetValue(key, out var value) ? value : string.Empty));
        settingsMock
            .Setup(service => service.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => settings.TryGetValue(key, out var value) ? value : null);

        return settingsMock.Object;
    }

    private static string SourceKey(int position, string line)
    {
        return BatchTranslationResponseMapper.GetSourceKey(new BatchSubtitleItem
        {
            Position = position,
            Line = line
        });
    }

    private sealed class StaticHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class CapturingDeepSeekHandler : HttpMessageHandler
    {
        public List<string> ChatBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ChatBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            var content = JsonSerializer.Serialize(new
            {
                translations = new[]
                {
                    new
                    {
                        position = 1,
                        sourceKey = SourceKey(1, "Hello there"),
                        line = "Czesc tam"
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    SseTestHelper.CreateOpenAiSseResponse(content),
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }
    }
}
