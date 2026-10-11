namespace UniNet.Domain.Entities;

public sealed class TaskEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskReportId { get; set; }
    public string FileUrl { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskReport TaskReport { get; set; } = null!;
}
