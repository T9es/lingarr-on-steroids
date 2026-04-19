namespace Lingarr.Server.Models.UploadWorkspace;

public class CreateUploadBatchRequest
{
    public string? Name { get; set; }
    public required string TargetLanguage { get; set; }
    public bool DefaultRemuxEnabled { get; set; }
}
