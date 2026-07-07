namespace Lingarr.Server.Models.UploadWorkspace;

public class UpdateUploadBatchRequest
{
    public required string Name { get; set; }
    public required string TargetLanguage { get; set; }
    public bool DefaultRemuxEnabled { get; set; }
}
