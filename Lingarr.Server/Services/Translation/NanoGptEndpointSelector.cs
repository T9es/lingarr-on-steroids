namespace Lingarr.Server.Services.Translation;

internal static class NanoGptEndpointSelector
{
    private const string ChatCompletionsEndpoint =
        "https://nano-gpt.com/api/v1/chat/completions";

    public static string GetChatCompletionsEndpoint()
    {
        return ChatCompletionsEndpoint;
    }

    public static string? GetBillingMode(bool subscriptionIncluded)
    {
        return subscriptionIncluded ? null : "paygo";
    }
}
