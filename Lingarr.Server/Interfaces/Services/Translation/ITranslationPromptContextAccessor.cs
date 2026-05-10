using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationPromptContextAccessor
{
    TranslationPromptContext? Current { get; set; }

    void Clear();
}
