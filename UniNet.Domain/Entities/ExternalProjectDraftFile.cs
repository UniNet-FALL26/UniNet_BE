namespace UniNet.Domain.Entities;

public sealed class ExternalProjectDraftFile
{
    public Guid DraftId { get; set; }
    public ExternalProjectDraft Draft { get; set; } = null!;
    public Guid UploadedFileId { get; set; }
    public UploadedFile UploadedFile { get; set; } = null!;
}
