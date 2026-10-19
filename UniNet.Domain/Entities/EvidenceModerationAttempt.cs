namespace UniNet.Domain.Entities;

public sealed class EvidenceModerationAttempt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ModerationId { get; init; }
    public int Number { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ResultsJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public bool Transient { get; set; }
}
