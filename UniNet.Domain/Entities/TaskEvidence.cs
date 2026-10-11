namespace UniNet.Domain.Entities;

public sealed class TaskEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskReportId { get; set; }
    public string FileUrl { get; set; } = null!;
    public string? PublicId { get; set; }
    public string? ResourceType { get; set; }
    public string? FileName { get; set; }
    public string? Format { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskReport TaskReport { get; set; } = null!;
}
