using System.Net;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Exceptions;

public static class TranslationFailureClassifier
{
    public static bool IsNonRepairableProviderConfigurationFailure(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is HttpRequestException httpException &&
                (httpException.StatusCode == HttpStatusCode.Unauthorized ||
                 httpException.StatusCode == HttpStatusCode.Forbidden))
            {
                return true;
            }

            var message = current.Message;
            if (message.Contains("api key not valid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("api key is invalid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not configured", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("version", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("provider", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsProviderUnavailable(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is ProviderCircuitOpenException)
            {
                return true;
            }

            if (current is HttpRequestException httpException &&
                IsRetryableProviderStatus(httpException.StatusCode))
            {
                return true;
            }

            if (current.Message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("status: serviceunavailable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetFailureSummary(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is HttpRequestException httpException &&
                IsRetryableProviderStatus(httpException.StatusCode) &&
                !string.IsNullOrWhiteSpace(current.Message))
            {
                return current.Message;
            }

            if (!string.IsNullOrWhiteSpace(current.Message) &&
                !current.Message.StartsWith("One or more errors occurred.", StringComparison.Ordinal))
            {
                return current.Message;
            }
        }

        return exception.Message;
    }

    private static bool IsRetryableProviderStatus(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               statusCode == HttpStatusCode.InternalServerError;
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
