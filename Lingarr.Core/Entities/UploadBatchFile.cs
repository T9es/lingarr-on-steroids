using Lingarr.Core.Enum;

namespace Lingarr.Core.Entities;

public class UploadBatchFile : BaseEntity
{
    public required int UploadBatchId { get; set; }
    public required UploadBatch UploadBatch { get; set; }
    public required UploadBatchFileKind FileKind { get; set; }
    public UploadBatchFileStatus Status { get; set; } = UploadBatchFileStatus.Uploaded;
    public required string Title { get; set; }
    public required string OriginalFileName { get; set; }
    public required string StoredPath { get; set; }
    public required string RelativeStoredPath { get; set; }
    public long FileSizeBytes { get; set; }
    public string? DetectedSourceLanguage { get; set; }
    public string? SelectedSourceLanguage { get; set; }
    public bool ExcludeFromTranslation { get; set; }
    public bool EmbedTranslatedSubtitle { get; set; }
    public int? SelectedEmbeddedStreamIndex { get; set; }
    public string? SelectedEmbeddedStreamLanguage { get; set; }
    public string? SelectedEmbeddedStreamTitle { get; set; }
    public string? SelectedEmbeddedStreamCodec { get; set; }
    public int? CurrentTranslationRequestId { get; set; }
    public DateTime? ProbeCompletedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ProbeError { get; set; }
    public string? LastError { get; set; }
    public List<UploadBatchFileSubtitleStream> SubtitleStreams { get; set; } = [];
    public List<UploadArtifact> Artifacts { get; set; } = [];
}
