using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations.Translation;

public class GenerateResponse
{
    [JsonPropertyName("model")] 
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("response")] 
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("done")] 
    public bool Done { get; set; }
}

public class ChatResponse
{
    [JsonPropertyName("choices")] 
    public List<ChatChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }
}

public class UsageInfo
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("message")] 
    public ChatMessage Message { get; set; } = new();
}

public class ChatMessage
{
    [JsonPropertyName("content")] 
    public string Content { get; set; } = string.Empty;
}