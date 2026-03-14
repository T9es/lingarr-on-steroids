using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations.Translation;

public class GeminiResponse
{
    [JsonPropertyName("candidates")] 
    public List<Candidate> Candidates { get; set; } = new();

    [JsonPropertyName("usageMetadata")]
    public UsageMetadataInfo? UsageMetadata { get; set; }

    public class UsageMetadataInfo
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }

    public class Candidate
    {
        [JsonPropertyName("content")] 
        public Content? Content { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("parts")] 
        public List<Part> Parts { get; set; } = new();
    }

    public class Part
    {
        [JsonPropertyName("text")] 
        public string Text { get; set; } = string.Empty;
    }
}

public class GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public GeminiError? Error { get; set; }
}

public class GeminiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("details")]
    public List<GeminiErrorDetail>? Details { get; set; }
}

public class GeminiErrorDetail
{
    [JsonPropertyName("@type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("retryDelay")]
    public string? RetryDelay { get; set; }
}