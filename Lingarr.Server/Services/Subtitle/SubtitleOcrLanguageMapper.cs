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

    /// <summary>
    /// Maps a subtitle language code to a Tesseract traineddata language name.
    /// Returns false for any language without an installed mapping so callers do
    /// not silently OCR with the wrong traineddata.
    /// </summary>
    public static bool TryMapToTesseractLanguage(string? languageCode, out string tesseractLanguage)
    {
        tesseractLanguage = string.Empty;
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalized = SubtitleLanguageHelper.NormalizeLanguageCode(languageCode);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            LanguageMap.TryGetValue(normalized, out var normalizedMapping))
        {
            tesseractLanguage = normalizedMapping;
            return true;
        }

        if (LanguageMap.TryGetValue(languageCode.Trim(), out var directMapping))
        {
            tesseractLanguage = directMapping;
            return true;
        }

        return false;
    }
}
