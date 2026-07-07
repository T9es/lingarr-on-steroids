using System.Text;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Services.Translation;

public sealed class TranslationPromptAugmenter : ITranslationPromptAugmenter
{
    private readonly ISettingService _settings;
    private readonly ITranslationPromptContextAccessor _contextAccessor;

    public TranslationPromptAugmenter(
        ISettingService settings,
        ITranslationPromptContextAccessor contextAccessor)
    {
        _settings = settings;
        _contextAccessor = contextAccessor;
    }

    public async Task<string> AugmentAsync(
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _contextAccessor.Current;
        if (context?.IsOcrDerivedSource != true)
        {
            return systemPrompt;
        }

        var enabled = await _settings.GetSetting(SettingKeys.SubtitleExtraction.OcrTranslationPromptEnabled);
        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            return systemPrompt;
        }

        var builder = new StringBuilder(systemPrompt.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("OCR-aware translation guidance:");
        builder.AppendLine("The source subtitle text was produced by OCR from image-based subtitles.");
        builder.AppendLine("Before translating each subtitle line, silently correct obvious OCR mistakes only when the correction is strongly implied by the line itself, nearby context, or media metadata.");
        builder.AppendLine("Do not invent missing dialogue, rewrite meaning, add missing content, summarize, or infer lines from memory.");
        builder.AppendLine("Preserve speaker meaning, tone, names, and punctuation unless punctuation is clearly OCR damage.");
        builder.AppendLine("If uncertain, translate the OCR text as written rather than inventing a correction.");
        builder.AppendLine();
        builder.AppendLine("Media context:");
        AppendContext(builder, context);

        return builder.ToString();
    }

    private static void AppendContext(StringBuilder builder, TranslationPromptContext context)
    {
        AppendIfPresent(builder, "Movie", context.MovieTitle);
        AppendIfPresent(builder, "Series", context.SeriesTitle);

        if (context.SeasonNumber.HasValue || context.EpisodeNumber.HasValue || !string.IsNullOrWhiteSpace(context.EpisodeTitle))
        {
            var episode = BuildEpisodeLabel(context);
            AppendIfPresent(builder, "Episode", episode);
        }

        AppendIfPresent(builder, "Source language", context.SourceLanguage);
        AppendIfPresent(builder, "Target language", context.TargetLanguage);
        AppendIfPresent(builder, "Source stream", context.SelectedStreamTitle);
        AppendIfPresent(builder, "Source subtitle type", context.SourceSubtitleType);
        AppendIfPresent(builder, "Source note", context.SourceNote);
    }

    private static string BuildEpisodeLabel(TranslationPromptContext context)
    {
        var parts = new List<string>();
        if (context.SeasonNumber.HasValue && context.EpisodeNumber.HasValue)
        {
            parts.Add($"S{context.SeasonNumber.Value:D2}E{context.EpisodeNumber.Value:D2}");
        }
        else if (context.EpisodeNumber.HasValue)
        {
            parts.Add($"E{context.EpisodeNumber.Value:D2}");
        }

        if (!string.IsNullOrWhiteSpace(context.EpisodeTitle))
        {
            parts.Add(context.EpisodeTitle);
        }

        return string.Join(" - ", parts);
    }

    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {label}: {value.Trim()}");
        }
    }
}
