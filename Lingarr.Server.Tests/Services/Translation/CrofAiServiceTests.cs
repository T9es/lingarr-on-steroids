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
using Lingarr.Server.Exceptions;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class CrofAiServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_ReturnsCorrectTranslations()
    {
        var handler = new CapturingCrofAiHandler();
        var service = CreateService(handler);

        var result = await service.TranslateBatchAsync(
            [
                new BatchSubtitleItem { Position = 1, Line = "Hello", SourceKey = "a1b2c3d4e5f6" },
                new BatchSubtitleItem { Position = 2, Line = "World", SourceKey = "f6e5d4c3b2a1" }
            ],
            "en",
            "es",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
        Assert.Single(handler.ChatBodies);

        using var requestBody = JsonDocument.Parse(handler.ChatBodies[0]);
        var responseFormat = requestBody.RootElement.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
        Assert.False(responseFormat.TryGetProperty("json_schema", out _));
    }

    [Fact]
    public async Task TranslateBatchAsync_EmptyContent_ThrowsClearError()
    {
        var handler = new CapturingCrofAiHandler(contentOverride: "");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateBatchAsync(
                [new BatchSubtitleItem { Position = 1, Line = "Hello" }],
                "en",
                "es",
                null,
                null,
                CancellationToken.None));

        Assert.Contains("CrofAI returned the response in the reasoning field", ex.Message);
        Assert.Contains("deepseek-v4-flash", ex.Message);
    }

    [Fact]
    public async Task TranslateBatchAsync_ArrayShape_ParsesCorrectly()
    {
        var handler = new CapturingCrofAiHandler(contentOverride: """
            [
                { "position": 10, "sourceKey": "k1l2m3n4o5p6", "line": "Bonjour" },
                { "position": 25, "sourceKey": "p6o5n4m3l2k1", "line": "Monde" }
            ]
            """);
        var service = CreateService(handler);

        var result = await service.TranslateBatchAsync(
            [
                new BatchSubtitleItem { Position = 10, Line = "Hello", SourceKey = "k1l2m3n4o5p6" },
                new BatchSubtitleItem { Position = 25, Line = "World", SourceKey = "p6o5n4m3l2k1" }
            ],
            "en",
            "fr",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Bonjour", result[10]);
        Assert.Equal("Monde", result[25]);
    }

    [Fact]
    public async Task TranslateBatchAsync_MarkdownFences_StripsCorrectly()
    {
        var handler = new CapturingCrofAiHandler(contentOverride: """
            ```json
            {
                "translations": [
                    { "position": 1, "sourceKey": "x1y2z3", "line": "Ciao" }
                ]
            }
            ```
            """);
        var service = CreateService(handler);

        var result = await service.TranslateBatchAsync(
            [new BatchSubtitleItem { Position = 1, Line = "Hello", SourceKey = "x1y2z3" }],
            "en",
            "it",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Ciao", result[1]);
    }

    private static CrofAiService CreateService(HttpMessageHandler handler)
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.CrofAi.ApiKey, "test-api-key" },
            { SettingKeys.Translation.CrofAi.Model, "deepseek-v4-flash" },
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
            .Setup(service => service.GetSetting(It.IsAny<string>()))
            .ReturnsAsync((string key) => settings.TryGetValue(key, out var value) ? value : null);
        settingsMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys.ToDictionary(
                key => key,
                key => settings.TryGetValue(key, out var value) ? value : string.Empty));

        var usageServiceMock = new Mock<ICrofAiUsageService>();
        usageServiceMock
            .Setup(service => service.EnsureRequestAllowedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        usageServiceMock
            .Setup(service => service.RecordRequestAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CrofAiService(
            settingsMock.Object,
            new Mock<ILogger<CrofAiService>>().Object,
            usageServiceMock.Object,
            new StaticHttpClientFactory(new HttpClient(handler)));
    }

    private sealed class StaticHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class CapturingCrofAiHandler : HttpMessageHandler
    {
        private readonly string? _contentOverride;

        public List<string> ChatBodies { get; } = [];

        public CapturingCrofAiHandler(string? contentOverride = null)
        {
            _contentOverride = contentOverride;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ChatBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            var content = _contentOverride ??
                JsonSerializer.Serialize(new
                {
                    translations = new[]
                    {
                        new { position = 1, sourceKey = "a1b2c3d4e5f6", line = "Hola" },
                        new { position = 2, sourceKey = "f6e5d4c3b2a1", line = "Mundo" }
                    }
                });

            var chatResponse = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-123",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new
                        {
                            role = "assistant",
                            content = content
                        },
                        finish_reason = "stop"
                    }
                },
                usage = new
                {
                    prompt_tokens = 12,
                    completion_tokens = 8,
                    total_tokens = 20
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(chatResponse, Encoding.UTF8, "application/json")
            };
        }
    }
}
