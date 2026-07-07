using System.Text.Json;

namespace Lingarr.Server.Models.UploadWorkspace;

public class UpdateUploadBatchFileRequest
{
    public string? SelectedSourceLanguage { get; set; }
    public bool ExcludeFromTranslation { get; set; }
    public bool EmbedTranslatedSubtitle { get; set; }
    public JsonElement SelectedEmbeddedStreamIndex { get; set; }

    public bool TryGetSelectedEmbeddedStreamIndex(out int? streamIndex)
    {
        switch (SelectedEmbeddedStreamIndex.ValueKind)
        {
            case JsonValueKind.Undefined:
                streamIndex = null;
                return false;
            case JsonValueKind.Null:
                streamIndex = null;
                return true;
            case JsonValueKind.Number when SelectedEmbeddedStreamIndex.TryGetInt32(out var parsedIndex):
                streamIndex = parsedIndex;
                return true;
            default:
                throw new InvalidOperationException(
                    "selectedEmbeddedStreamIndex must be an integer or null when provided.");
        }
    }
}
