namespace UniNet.Domain.Entities;

public sealed class ReportAuditLog
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public int Version { get; set; }
    public string Action { get; set; } = null!;
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Reason { get; set; }
}
