namespace Lingarr.Server.Services.Subtitle;

public static class SubtitleOcrLanguageMapper
{
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng",
        ["eng"] = "eng",
        ["ja"] = "jpn",
        ["jpn"] = "jpn",
        ["jp"] = "jpn",
        ["fr"] = "fra",
        ["fre"] = "fra",
        ["fra"] = "fra",
        ["es"] = "spa",
        ["spa"] = "spa",
        ["de"] = "deu",
        ["deu"] = "deu",
        ["ger"] = "deu",
        ["pl"] = "pol",
        ["pol"] = "pol"
    };

    public static string MapToTesseractLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "eng";
        }

        var normalized = SubtitleLanguageHelper.NormalizeLanguageCode(languageCode);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            LanguageMap.TryGetValue(normalized, out var mapped))
        {
            return mapped;
        }

        return LanguageMap.TryGetValue(languageCode.Trim(), out mapped)
            ? mapped
            : "eng";
    }
}
