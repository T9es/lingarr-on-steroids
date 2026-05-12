using System.Net;
using System.Net.Http;
using Lingarr.Server.Exceptions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationFailureClassifierTests
{
    [Theory]
    [InlineData("API key not valid. Please pass a valid API key.")]
    [InlineData("Gemini API key or model is not configured.")]
    [InlineData("Anthropic API key, model or version is not configured.")]
    [InlineData("Unauthorized")]
    [InlineData("Forbidden")]
    public void IsNonRepairableProviderConfigurationFailure_ShouldDetectAuthAndConfigurationErrors(string message)
    {
        var exception = new TranslationException(message);

        Assert.True(TranslationFailureClassifier.IsNonRepairableProviderConfigurationFailure(exception));
    }

    [Fact]
    public void IsNonRepairableProviderConfigurationFailure_ShouldDetectAuthStatusCodes()
    {
        var exception = new HttpRequestException("Provider rejected the request.", null, HttpStatusCode.Unauthorized);

        Assert.True(TranslationFailureClassifier.IsNonRepairableProviderConfigurationFailure(exception));
    }

    [Fact]
    public void IsNonRepairableProviderConfigurationFailure_ShouldIgnoreRepairableResponseErrors()
    {
        var exception = new TranslationException("Failed to parse translated subtitles");

        Assert.False(TranslationFailureClassifier.IsNonRepairableProviderConfigurationFailure(exception));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void IsProviderUnavailable_ShouldDetectProviderOutageStatusCodes(HttpStatusCode statusCode)
    {
        var exception = new HttpRequestException("Provider error", null, statusCode);

        Assert.True(TranslationFailureClassifier.IsProviderUnavailable(exception));
    }

    [Fact]
    public void IsProviderUnavailable_ShouldDetectProviderOutageViaInnerException()
    {
        var outer = new TranslationException(
            "Provider is temporarily unavailable. Retry limit reached.",
            new HttpRequestException("Provider server error", null, HttpStatusCode.ServiceUnavailable));

        Assert.True(TranslationFailureClassifier.IsProviderUnavailable(outer));
    }

    [Fact]
    public void IsProviderUnavailable_ShouldNotMatchNonErrorStatus()
    {
        var exception = new HttpRequestException("Not found", null, HttpStatusCode.NotFound);

        Assert.False(TranslationFailureClassifier.IsProviderUnavailable(exception));
    }

    [Fact]
    public void IsProviderUnavailable_ShouldDetectTemporarilyUnavailableMessage()
    {
        var exception = new TranslationException("The service is temporarily unavailable.");

        Assert.True(TranslationFailureClassifier.IsProviderUnavailable(exception));
    }
}
