using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Translation;

namespace Lingarr.Server.Services.Translation;

public sealed class TranslationPromptContextAccessor : ITranslationPromptContextAccessor
{
    public TranslationPromptContext? Current { get; set; }

    public void Clear()
    {
        Current = null;
    }
}
