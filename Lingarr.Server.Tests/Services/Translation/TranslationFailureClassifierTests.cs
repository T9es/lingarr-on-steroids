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
}
