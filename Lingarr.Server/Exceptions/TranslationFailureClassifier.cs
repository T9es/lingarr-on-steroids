using System.Net;

namespace Lingarr.Server.Exceptions;

public static class TranslationFailureClassifier
{
    public static bool IsProviderUnavailable(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
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
               statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
