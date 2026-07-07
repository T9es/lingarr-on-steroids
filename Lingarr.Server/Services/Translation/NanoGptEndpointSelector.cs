namespace Lingarr.Server.Services.Translation;

internal static class NanoGptEndpointSelector
{
    private const string SubscriptionChatCompletionsEndpoint =
        "https://nano-gpt.com/api/subscription/v1/chat/completions";

    private const string PayAsYouGoChatCompletionsEndpoint =
        "https://nano-gpt.com/api/v1/chat/completions";

    public static string GetChatCompletionsEndpoint(bool subscriptionIncluded)
    {
        return subscriptionIncluded
            ? SubscriptionChatCompletionsEndpoint
            : PayAsYouGoChatCompletionsEndpoint;
    }

    public static string? GetBillingMode(bool subscriptionIncluded)
    {
        return subscriptionIncluded ? null : "paygo";
    }
}
