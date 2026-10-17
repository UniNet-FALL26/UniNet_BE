using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

// Source identifiers live in an immutable snapshot, not cascading FKs to mutable task data.
public sealed class ProjectEvidenceModeration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public int Version { get; init; }
    public string SourceHash { get; init; } = null!;
    public string SnapshotJson { get; init; } = null!;
    public string PolicyHash { get; init; } = null!;
    public string PolicyJson { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
    public EvidenceModerationStatus Status { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastError { get; set; }
    public string? Summary { get; set; }
    public List<EvidenceModerationAttempt> Attempts { get; set; } = [];
}

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
