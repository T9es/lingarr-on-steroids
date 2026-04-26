using Lingarr.Server.Models;

namespace Lingarr.Server.Services.Translation;

internal static class NanoGptModelCatalog
{
    public static List<LabelValue> BuildModelOptions(
        IReadOnlyCollection<ModelData> subscriptionModels,
        IReadOnlyCollection<ModelData> paidModels)
    {
        var options = new List<LabelValue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in subscriptionModels.OrderBy(model => model.Id))
        {
            if (seen.Add(model.Id))
            {
                options.Add(new LabelValue
                {
                    Label = FormatLabel(model, "Subscription"),
                    Value = model.Id
                });
            }
        }

        foreach (var model in paidModels.OrderBy(model => model.Id))
        {
            if (seen.Add(model.Id))
            {
                options.Add(new LabelValue
                {
                    Label = FormatLabel(model, "Paid"),
                    Value = model.Id
                });
            }
        }

        return options;
    }

    public static bool SupportsStructuredOutput(ModelData? model)
    {
        return model?.Capabilities?.StructuredOutput != false;
    }

    private static string FormatLabel(ModelData model, string billingLabel)
    {
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(model.Name) ? model.Id : $"{model.Name} ({model.Id})",
            billingLabel
        };

        if (model.ContextLength is > 0)
        {
            parts.Add($"{FormatTokenCount(model.ContextLength.Value)} ctx");
        }

        if (model.MaxOutputTokens is > 0)
        {
            parts.Add($"{FormatTokenCount(model.MaxOutputTokens.Value)} out");
        }

        parts.Add(SupportsStructuredOutput(model) ? "structured" : "no structured output");

        if (model.Pricing?.Prompt is { } prompt && model.Pricing?.Completion is { } completion)
        {
            parts.Add($"${prompt:0.####}/${completion:0.####} per MTok");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatTokenCount(int tokens)
    {
        if (tokens >= 1000 && tokens % 1000 == 0)
        {
            return $"{tokens / 1000}K";
        }

        return tokens.ToString("N0");
    }
}
