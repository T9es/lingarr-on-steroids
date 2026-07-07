using Lingarr.Core.Entities;

namespace Lingarr.Server.Services.Subtitle;

internal static class EmbeddedTargetSubtitleHelper
{
    private const int MinimumFullDialogueScore = 30;

    public static HashSet<string> GetSatisfiedTargetLanguages(
        IReadOnlyCollection<EmbeddedSubtitle> embeddedSubtitles,
        IReadOnlyCollection<string> targetLanguages)
    {
        var satisfiedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var embeddedSubtitle in embeddedSubtitles)
        {
            if (!IsQualifiedTarget(embeddedSubtitle, targetLanguages, out var targetLanguage))
            {
                continue;
            }

            satisfiedLanguages.Add(targetLanguage);
        }

        return satisfiedLanguages;
    }

    public static bool IsSatisfiedTargetLanguage(
        IReadOnlySet<string> satisfiedTargetLanguages,
        string targetLanguage)
    {
        var normalizedTargetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage);
        return !string.IsNullOrWhiteSpace(normalizedTargetLanguage) &&
               satisfiedTargetLanguages.Contains(normalizedTargetLanguage);
    }

    private static bool IsQualifiedTarget(
        EmbeddedSubtitle embeddedSubtitle,
        IReadOnlyCollection<string> targetLanguages,
        out string normalizedTargetLanguage)
    {
        normalizedTargetLanguage = string.Empty;

        if (!embeddedSubtitle.IsTextBased || string.IsNullOrWhiteSpace(embeddedSubtitle.Language))
        {
            return false;
        }

        foreach (var targetLanguage in targetLanguages)
        {
            if (!SubtitleLanguageHelper.LanguageMatches(embeddedSubtitle.Language, targetLanguage))
            {
                continue;
            }

            if (!IsLingarrGeneratedTarget(embeddedSubtitle) &&
                SubtitleLanguageHelper.ScoreSubtitleCandidate(embeddedSubtitle, targetLanguage) < MinimumFullDialogueScore)
            {
                return false;
            }

            normalizedTargetLanguage = SubtitleLanguageHelper.NormalizeLanguageCode(targetLanguage) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedTargetLanguage);
        }

        return false;
    }

    private static bool IsLingarrGeneratedTarget(EmbeddedSubtitle embeddedSubtitle)
    {
        return embeddedSubtitle.Title?.Contains("(Lingarr)", StringComparison.OrdinalIgnoreCase) == true;
    }
}
