using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Models;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Models.NanoGpt;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class NanoGptServiceTests
{
    [Fact]
    public void BuildModelOptions_CombinesSubscriptionAndPaidModelsWithCapabilityLabels()
    {
        var subscriptionModels = new List<ModelData>
        {
            new()
            {
                Id = "openai/gpt-5-mini",
                Name = "GPT-5 Mini",
                ContextLength = 128000,
                MaxOutputTokens = 16000,
                Capabilities = new ModelCapabilities { StructuredOutput = true }
            }
        };
        var paidModels = new List<ModelData>
        {
            new()
            {
                Id = "anthropic/claude-opus",
                Name = "Claude Opus",
                ContextLength = 200000,
                MaxOutputTokens = 8192,
                Capabilities = new ModelCapabilities { StructuredOutput = false }
            },
            new() { Id = "openai/gpt-5-mini", Name = "Duplicate" }
        };

        var options = NanoGptModelCatalog.BuildModelOptions(subscriptionModels, paidModels);

        Assert.Equal(2, options.Count);
        Assert.Equal("openai/gpt-5-mini", options[0].Value);
        Assert.Contains("Subscription", options[0].Label);
        Assert.Contains("128K ctx", options[0].Label);
        Assert.Contains("structured", options[0].Label);
        Assert.Equal("anthropic/claude-opus", options[1].Value);
        Assert.Contains("Paid", options[1].Label);
        Assert.Contains("200K ctx", options[1].Label);
        Assert.Contains("no structured output", options[1].Label);
    }

    [Fact]
    public void ParseUsage_MapsDailyMonthlyAndTokenWindows()
    {
        using var document = JsonDocument.Parse("""
        {
          "active": true,
          "limits": { "daily": 5000, "monthly": 60000 },
          "daily": { "used": 125, "remaining": 4875, "percentUsed": 0.025, "resetAt": 1738540800000 },
          "monthly": { "used": 1000, "remaining": 59000, "percentUsed": 0.0166667, "resetAt": 1739404800000 },
          "tokenUsage": {
            "weekly": { "used": 504750, "limit": 60000000, "remaining": 59495250, "resetAt": 1738627200000 }
          },
          "state": "active"
        }
        """);

        var usage = NanoGptUsageParser.Parse(document.RootElement);

        Assert.True(usage.Active);
        Assert.Equal(5000, usage.Daily.Limit);
        Assert.Equal(125, usage.Daily.Used);
        Assert.Equal(4875, usage.Daily.Remaining);
        Assert.Equal(60000, usage.Monthly.Limit);
        Assert.Equal(1000, usage.Monthly.Used);
        Assert.Equal(60000000, usage.WeeklyTokens.Limit);
        Assert.Equal(504750, usage.WeeklyTokens.Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1738627200000).UtcDateTime, usage.WeeklyTokens.ResetAt);
    }

    [Fact]
    public void ReservePolicy_BlocksWhenAnyReserveWouldBeConsumed()
    {
        var usage = new NanoGptUsageSnapshot
        {
            Active = true,
            Daily = new NanoGptUsageWindow { Limit = 100, Used = 91, Remaining = 9 },
            Monthly = new NanoGptUsageWindow { Limit = 1000, Used = 800, Remaining = 200 },
            WeeklyTokens = new NanoGptUsageWindow { Limit = 60000000, Used = 59000001, Remaining = 999999 }
        };

        var result = NanoGptReservePolicy.Evaluate(
            usage,
            new NanoGptReserveSettings
            {
                DailyUnitReserve = 10,
                MonthlyUnitReserve = 50,
                TokenReserve = 1000000
            });

        Assert.True(result.IsBlocked);
        Assert.Contains("daily", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "https://nano-gpt.com/api/subscription/v1/chat/completions")]
    [InlineData(false, "https://nano-gpt.com/api/v1/chat/completions")]
    public void GetChatCompletionsEndpoint_RoutesByNanoGptBillingCoverage(
        bool subscriptionIncluded,
        string expectedEndpoint)
    {
        var endpoint = NanoGptEndpointSelector.GetChatCompletionsEndpoint(subscriptionIncluded);

        Assert.Equal(expectedEndpoint, endpoint);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "paygo")]
    public void GetBillingMode_ForcesPayGoOnlyForPaidModels(bool subscriptionIncluded, string? expectedBillingMode)
    {
        var billingMode = NanoGptEndpointSelector.GetBillingMode(subscriptionIncluded);

        Assert.Equal(expectedBillingMode, billingMode);
    }

    [Fact]
    public void SupportsStructuredOutput_RequiresExplicitNanoGptCapability()
    {
        Assert.True(NanoGptModelCatalog.SupportsStructuredOutput(new ModelData
        {
            Capabilities = new ModelCapabilities { StructuredOutput = true }
        }));

        Assert.False(NanoGptModelCatalog.SupportsStructuredOutput(new ModelData()));
        Assert.False(NanoGptModelCatalog.SupportsStructuredOutput(new ModelData
        {
            Capabilities = new ModelCapabilities { StructuredOutput = false }
        }));
        Assert.True(NanoGptModelCatalog.SupportsStructuredOutput(new ModelData
        {
            Id = "deepseek/deepseek-v4-pro-cheaper:thinking",
            Capabilities = new ModelCapabilities { StructuredOutput = true }
        }));
    }

    [Fact]
    public void UsesJsonObjectBatch_DetectsNanoGptReasoningModelSuffixes()
    {
        Assert.True(NanoGptModelCatalog.UsesJsonObjectBatch("deepseek/deepseek-v4-pro-cheaper:thinking"));
        Assert.True(NanoGptModelCatalog.UsesJsonObjectBatch("model/name:reasoning"));
        Assert.False(NanoGptModelCatalog.UsesJsonObjectBatch("openai/gpt-5-mini"));
    }

    [Fact]
    public async Task TranslateBatchAsync_UsesJsonObjectBatchForThinkingModels()
    {
        const string modelId = "deepseek/deepseek-v4-pro-cheaper:thinking";
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.NanoGpt.ApiKey, "test-api-key" },
            { SettingKeys.Translation.NanoGpt.Model, modelId },
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

        var usageServiceMock = new Mock<INanoGptUsageService>();
        usageServiceMock
            .Setup(service => service.EnsureUsageAvailableAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CapturingNanoGptHandler(modelId);
        var httpClient = new HttpClient(handler);
        var service = new NanoGptService(
            settingsMock.Object,
            new Mock<ILogger<NanoGptService>>().Object,
            usageServiceMock.Object,
            new StaticHttpClientFactory(httpClient),
            new MemoryCache(new MemoryCacheOptions()));

        var result = await service.TranslateBatchAsync(
            [
                new BatchSubtitleItem { Position = 1, Line = "Hello" },
                new BatchSubtitleItem { Position = 2, Line = "World" }
            ],
            "en",
            "es",
            null,
            null,
            CancellationToken.None);

        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
        Assert.Single(handler.ChatRequests);
        Assert.Equal("/api/subscription/v1/chat/completions", handler.ChatRequests[0].RequestUri?.AbsolutePath);

        using var requestBody = JsonDocument.Parse(handler.ChatBodies[0]);
        var responseFormat = requestBody.RootElement.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
        Assert.False(responseFormat.TryGetProperty("json_schema", out _));
        Assert.True(requestBody.RootElement.GetProperty("reasoning").GetProperty("exclude").GetBoolean());
    }

    private sealed class StaticHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return httpClient;
        }
    }

    private sealed class CapturingNanoGptHandler(string modelId) : HttpMessageHandler
    {
        public List<HttpRequestMessage> ChatRequests { get; } = [];
        public List<string> ChatBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/api/subscription/v1/models")
            {
                var modelsResponse = JsonSerializer.Serialize(new
                {
                    data = new[]
                    {
                        new
                        {
                            id = modelId,
                            capabilities = new
                            {
                                structured_output = true,
                                reasoning = true
                            }
                        }
                    }
                });

                return JsonResponse(modelsResponse);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/api/subscription/v1/chat/completions")
            {
                ChatRequests.Add(request);
                ChatBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

                var translatedContent = JsonSerializer.Serialize(new
                {
                    translations = new[]
                    {
                        new { position = 1, line = "Hola" },
                        new { position = 2, line = "Mundo" }
                    }
                });
                var chatResponse = JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            message = new
                            {
                                role = "assistant",
                                content = translatedContent
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

                return JsonResponse(chatResponse);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage JsonResponse(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
