using System;
using System.Collections.Generic;
using System.Text.Json;
using Lingarr.Server.Models;
using Lingarr.Server.Models.NanoGpt;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class NanoGptServiceTests
{
    [Fact]
    public void BuildModelOptions_CombinesSubscriptionAndPaidModelsWithCapabilityLabels()
    {
        var subscriptionModels = new List<ModelData>
        {
            new()
            {
                Id = "openai/gpt-5-mini",
                Name = "GPT-5 Mini",
                ContextLength = 128000,
                MaxOutputTokens = 16000,
                Capabilities = new ModelCapabilities { StructuredOutput = true }
            }
        };
        var paidModels = new List<ModelData>
        {
            new()
            {
                Id = "anthropic/claude-opus",
                Name = "Claude Opus",
                ContextLength = 200000,
                MaxOutputTokens = 8192,
                Capabilities = new ModelCapabilities { StructuredOutput = false }
            },
            new() { Id = "openai/gpt-5-mini", Name = "Duplicate" }
        };

        var options = NanoGptModelCatalog.BuildModelOptions(subscriptionModels, paidModels);

        Assert.Equal(2, options.Count);
        Assert.Equal("openai/gpt-5-mini", options[0].Value);
        Assert.Contains("Subscription", options[0].Label);
        Assert.Contains("128K ctx", options[0].Label);
        Assert.Contains("structured", options[0].Label);
        Assert.Equal("anthropic/claude-opus", options[1].Value);
        Assert.Contains("Paid", options[1].Label);
        Assert.Contains("200K ctx", options[1].Label);
        Assert.Contains("no structured output", options[1].Label);
    }

    [Fact]
    public void ParseUsage_MapsDailyMonthlyAndTokenWindows()
    {
        using var document = JsonDocument.Parse("""
        {
          "active": true,
          "limits": { "daily": 5000, "monthly": 60000 },
          "daily": { "used": 125, "remaining": 4875, "percentUsed": 0.025, "resetAt": 1738540800000 },
          "monthly": { "used": 1000, "remaining": 59000, "percentUsed": 0.0166667, "resetAt": 1739404800000 },
          "tokenUsage": {
            "weekly": { "used": 504750, "limit": 60000000, "remaining": 59495250, "resetAt": 1738627200000 }
          },
          "state": "active"
        }
        """);

        var usage = NanoGptUsageParser.Parse(document.RootElement);

        Assert.True(usage.Active);
        Assert.Equal(5000, usage.Daily.Limit);
        Assert.Equal(125, usage.Daily.Used);
        Assert.Equal(4875, usage.Daily.Remaining);
        Assert.Equal(60000, usage.Monthly.Limit);
        Assert.Equal(1000, usage.Monthly.Used);
        Assert.Equal(60000000, usage.WeeklyTokens.Limit);
        Assert.Equal(504750, usage.WeeklyTokens.Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1738627200000).UtcDateTime, usage.WeeklyTokens.ResetAt);
    }

    [Fact]
    public void ReservePolicy_BlocksWhenAnyReserveWouldBeConsumed()
    {
        var usage = new NanoGptUsageSnapshot
        {
            Active = true,
            Daily = new NanoGptUsageWindow { Limit = 100, Used = 91, Remaining = 9 },
            Monthly = new NanoGptUsageWindow { Limit = 1000, Used = 800, Remaining = 200 },
            WeeklyTokens = new NanoGptUsageWindow { Limit = 60000000, Used = 59000001, Remaining = 999999 }
        };

        var result = NanoGptReservePolicy.Evaluate(
            usage,
            new NanoGptReserveSettings
            {
                DailyUnitReserve = 10,
                MonthlyUnitReserve = 50,
                TokenReserve = 1000000
            });

        Assert.True(result.IsBlocked);
        Assert.Contains("daily", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, "https://nano-gpt.com/api/subscription/v1/chat/completions")]
    [InlineData(false, "https://nano-gpt.com/api/v1/chat/completions")]
    public void GetChatCompletionsEndpoint_UsesSubscriptionEndpointOnlyForIncludedModels(
        bool subscriptionIncluded,
        string expectedEndpoint)
    {
        var endpoint = NanoGptEndpointSelector.GetChatCompletionsEndpoint(subscriptionIncluded);

        Assert.Equal(expectedEndpoint, endpoint);
    }
}
