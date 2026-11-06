namespace UniNet.Domain.Entities;

public sealed class ProjectEvidenceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid UploadedFileId { get; set; }
    public UploadedFile UploadedFile { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
