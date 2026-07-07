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
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Tests.Data;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class AnthropicServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_UsesSafeDefaultMaxTokensInsteadOfLegacy1024()
    {
        var handler = new CapturingAnthropicHandler();
        var service = new AnthropicService(
            CreateSettingService(),
            new HttpClient(handler),
            new Mock<ILogger<AnthropicService>>().Object);

        var result = await service.TranslateBatchAsync(
            [new BatchSubtitleItem { Position = 1, Line = "Hello there" }],
            "en",
            "pl",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Czesc tam", result[1]);
        var body = Assert.Single(handler.MessageBodies);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(8192, json.RootElement.GetProperty("max_tokens").GetInt32());
    }

    private static ISettingService CreateSettingService()
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.Anthropic.ApiKey, "test-api-key" },
            { SettingKeys.Translation.Anthropic.Model, "claude-test" },
            { SettingKeys.Translation.Anthropic.Version, "2023-06-01" },
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

    private sealed class CapturingAnthropicHandler : HttpMessageHandler
    {
        public List<string> MessageBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            MessageBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            var toolInput = JsonSerializer.Serialize(new
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
                    SseTestHelper.CreateAnthropicSseResponse(toolInput),
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }
    }
}
