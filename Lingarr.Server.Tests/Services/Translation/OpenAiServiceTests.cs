using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Batch;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class OpenAiServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_ShouldStopInFlightRetriesWhenCircuitOpens()
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.OpenAi.ApiKey, "test-api-key" },
            { SettingKeys.Translation.OpenAi.Model, "test-model" },
            { SettingKeys.Translation.AiPrompt, "Translate from {sourceLanguage} to {targetLanguage}." },
            { SettingKeys.Translation.AiContextPrompt, "Context." },
            { SettingKeys.Translation.AiContextPromptEnabled, "false" },
            { SettingKeys.Translation.CustomAiParameters, "[]" },
            { SettingKeys.Translation.RequestTimeout, "30" },
            { SettingKeys.Translation.MaxRetries, "9999" },
            { SettingKeys.Translation.RetryDelay, "0" },
            { SettingKeys.Translation.RetryDelayMultiplier, "1" }
        };
        var settingsMock = new Mock<ISettingService>();
        settingsMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> keys) => keys.ToDictionary(
                key => key,
                key => settings.TryGetValue(key, out var value) ? value : string.Empty));

        var handler = new CountingStatusHandler(HttpStatusCode.InternalServerError);
        var breaker = new ProviderCircuitBreaker(new Mock<ILogger<ProviderCircuitBreaker>>().Object);
        var service = new OpenAiService(
            settingsMock.Object,
            new Mock<ILogger<OpenAiService>>().Object,
            new HttpClient(handler),
            circuitBreaker: breaker);

        var exception = await Assert.ThrowsAsync<ProviderCircuitOpenException>(() =>
            service.TranslateBatchAsync(
                [new BatchSubtitleItem { Position = 1, Line = "Hello" }],
                "en",
                "pl",
                null,
                null,
                CancellationToken.None));

        Assert.Equal("openai", exception.ProviderName);
        Assert.Equal(3, handler.RequestCount);
    }

    private sealed class CountingStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":500,\"message\":\"Failed to generate response\",\"type\":\"internal_error\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
