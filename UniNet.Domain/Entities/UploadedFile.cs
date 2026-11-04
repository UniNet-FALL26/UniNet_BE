namespace UniNet.Domain.Entities;

public sealed class UploadedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerAccountId { get; set; }
    public Account OwnerAccount { get; set; } = null!;
    public string PublicId { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string Format { get; set; } = null!;
    public string ResourceType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExternalProjectDraftFile> DraftFiles { get; set; } = [];
}
