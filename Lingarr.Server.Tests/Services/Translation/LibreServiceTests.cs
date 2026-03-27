using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class LibreServiceTests
{
    [Fact]
    public async Task TranslateAsync_ShouldRetryOnRateLimitAndServiceUnavailable()
    {
        var settingsMock = new Mock<ISettingService>();
        settingsMock.Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Translation.LibreTranslate.Url] = "http://localhost:5000",
                [SettingKeys.Translation.LibreTranslate.ApiKey] = "secret",
                [SettingKeys.Translation.MaxRetries] = "3",
                [SettingKeys.Translation.RetryDelay] = "0",
                [SettingKeys.Translation.RetryDelayMultiplier] = "1"
            });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.TooManyRequests,
                Content = new StringContent("rate limited")
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new StringContent("temporarily unavailable")
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    JsonSerializer.Serialize(new { translatedText = "czesc" }),
                    Encoding.UTF8,
                    "application/json")
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        var service = new LibreService(
            httpClient,
            settingsMock.Object,
            Mock.Of<ILogger<LibreService>>());

        var result = await service.TranslateAsync("hello", "en", "pl", null, null, CancellationToken.None);

        Assert.Equal("czesc", result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(3),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
