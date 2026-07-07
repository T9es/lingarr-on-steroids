using System.Collections.Generic;
using System.Text.Json;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class StructuredJsonResponseSanitizerTests
{
    [Fact]
    public void SanitizeInvalidEscapes_RepairsRawAssLineBreakEscape()
    {
        const string json = """{"translations":[{"position":1,"sourceKey":"abc","line":"Pierwsza\NDruga"}]}""";

        var result = StructuredJsonResponseSanitizer.SanitizeInvalidEscapes(json);

        Assert.True(result.WasModified);
        Assert.Contains("""Pierwsza\\NDruga""", result.Json);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result.Json);
        var line = parsed
            .GetProperty("translations")[0]
            .GetProperty("line")
            .GetString();
        Assert.Equal(@"Pierwsza\NDruga", line);
    }

    [Fact]
    public void SanitizeInvalidEscapes_PreservesValidJsonEscapes()
    {
        const string json = """{"line":"Pierwsza\nDruga","slash":"Pierwsza\\NDruga","unicode":"\u0041","quote":"\""}""";

        var result = StructuredJsonResponseSanitizer.SanitizeInvalidEscapes(json);

        Assert.False(result.WasModified);
        Assert.Equal(json, result.Json);
    }

    [Fact]
    public void SanitizeInvalidEscapes_AllowsStructuredBatchParsingAfterRepair()
    {
        const string json = """[{"position":7,"sourceKey":"abc123","line":"Linia A\NLinia B"}]""";

        var sanitized = StructuredJsonResponseSanitizer.SanitizeInvalidEscapes(json);
        var parsed = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(sanitized.Json);

        var item = Assert.Single(parsed!);
        Assert.Equal(7, item.Position);
        Assert.Equal("abc123", item.SourceKey);
        Assert.Equal(@"Linia A\NLinia B", item.Line);
    }
}
