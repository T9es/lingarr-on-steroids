namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationPromptAugmenter
{
    Task<string> AugmentAsync(string systemPrompt, CancellationToken cancellationToken = default);
}
