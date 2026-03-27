using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using GTranslate.Results;
using GTranslate.Translators;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class GTranslatorServiceTests
{
    [Fact]
    public async Task TranslateAsync_ShouldRetryOnTooManyRequestsAndServiceUnavailable()
    {
        var settingsMock = new Mock<ISettingService>();
        settingsMock.Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Translation.MaxRetries] = "3",
                [SettingKeys.Translation.RetryDelay] = "0",
                [SettingKeys.Translation.RetryDelayMultiplier] = "1"
            });

        var translatorMock = new Mock<ITranslator>();
        var translationResultMock = new Mock<ITranslationResult>();
        translationResultMock.SetupGet(r => r.Translation).Returns("czesc");

        translatorMock.SetupSequence(t => t.TranslateAsync("hello", "pl", "en"))
            .ThrowsAsync(new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests))
            .ThrowsAsync(new HttpRequestException("service unavailable", null, HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(translationResultMock.Object);

        var service = new GTranslatorService<ITranslator>(
            translatorMock.Object,
            "/app/Statics/google_languages.json",
            settingsMock.Object,
            Mock.Of<ILogger>());

        var result = await service.TranslateAsync("hello", "en", "pl", null, null, CancellationToken.None);

        Assert.Equal("czesc", result);
        translatorMock.Verify(t => t.TranslateAsync("hello", "pl", "en"), Times.Exactly(3));
    }

    [Fact]
    public async Task TranslateAsync_ShouldThrowWhenRetryLimitIsReached()
    {
        var settingsMock = new Mock<ISettingService>();
        settingsMock.Setup(s => s.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Translation.MaxRetries] = "2",
                [SettingKeys.Translation.RetryDelay] = "0",
                [SettingKeys.Translation.RetryDelayMultiplier] = "1"
            });

        var translatorMock = new Mock<ITranslator>();
        translatorMock.Setup(t => t.TranslateAsync("hello", "pl", "en"))
            .ThrowsAsync(new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests));

        var service = new GTranslatorService<ITranslator>(
            translatorMock.Object,
            "/app/Statics/google_languages.json",
            settingsMock.Object,
            Mock.Of<ILogger>());

        await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateAsync("hello", "en", "pl", null, null, CancellationToken.None));
    }
}
