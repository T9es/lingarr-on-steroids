using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Exceptions;

public class MissingTranslationException : TranslationException
{
    public MissingTranslationException(
        IReadOnlyList<MissingTranslationCue> missingCues,
        Exception? exception = null)
        : base(BuildMessage(missingCues), exception)
    {
        MissingCues = missingCues;
    }

    public IReadOnlyList<MissingTranslationCue> MissingCues { get; }

    private static string BuildMessage(IReadOnlyList<MissingTranslationCue> missingCues)
    {
        var positionRange = missingCues.Count <= 10
            ? string.Join(", ", missingCues.Select(item => item.Position))
            : $"{string.Join(", ", missingCues.Take(10).Select(item => item.Position))}... (+{missingCues.Count - 10} more)";

        var examples = missingCues.Take(5)
            .Select(item =>
            {
                var text = item.SourceText.Length > 120
                    ? item.SourceText[..117] + "..."
                    : item.SourceText;
                return $"pos {item.Position}: \"{text}\"";
            });
        var exampleText = string.Join("; ", examples);

        return $"Translation failed: {missingCues.Count} subtitle(s) missing at positions: {positionRange}. First examples: {exampleText}";
    }
}
