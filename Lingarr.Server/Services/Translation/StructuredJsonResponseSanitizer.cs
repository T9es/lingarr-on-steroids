namespace Lingarr.Server.Services.Translation;

internal sealed record StructuredJsonSanitizationResult(string Json, bool WasModified);

internal static class StructuredJsonResponseSanitizer
{
    private static readonly HashSet<char> ValidEscapes = ['"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u'];

    public static StructuredJsonSanitizationResult SanitizeInvalidEscapes(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new StructuredJsonSanitizationResult(json, false);
        }

        var builder = new System.Text.StringBuilder(json.Length);
        var inString = false;
        var modified = false;

        for (var index = 0; index < json.Length; index++)
        {
            var current = json[index];

            if (!inString)
            {
                builder.Append(current);
                if (current == '"')
                {
                    inString = true;
                }

                continue;
            }

            if (current == '"')
            {
                builder.Append(current);
                inString = false;
                continue;
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (index + 1 >= json.Length)
            {
                builder.Append(@"\\");
                modified = true;
                continue;
            }

            var next = json[index + 1];
            if (ValidEscapes.Contains(next))
            {
                builder.Append(current);
                builder.Append(next);
                index++;
                continue;
            }

            builder.Append(@"\\");
            modified = true;
        }

        return new StructuredJsonSanitizationResult(
            modified ? builder.ToString() : json,
            modified);
    }

    public static string SanitizeInvalidEscapes(string json, ILogger logger, string providerName)
    {
        var result = SanitizeInvalidEscapes(json);
        if (result.WasModified)
        {
            logger.LogWarning(
                "{Provider} returned JSON with invalid string escape sequence(s). Repaired mechanical JSON escapes before parsing.",
                providerName);
        }

        return result.Json;
    }
}
