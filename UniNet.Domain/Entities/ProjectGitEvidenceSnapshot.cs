namespace UniNet.Domain.Entities;

// Capture membership is frozen on finalization; it does not grant access to evidence facts.
public sealed class ProjectGitEvidenceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ObservationCutoff { get; set; }
    public int CapturePolicyVersion { get; set; } = 1;
    public bool Finalized { get; set; }
}

public sealed class ProjectGitEvidenceSnapshotEntry
{
    public Guid SnapshotId { get; set; }
    public Guid ProjectEvidenceObservationId { get; set; }
    public Guid GitEvidenceVersionId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectGitRepositoryId { get; set; }
}

// A capture-time receipt, never a synthetic GitHub activity. All values are
// copied from completed job/checkpoint rows visible at the capture's MVCC point.
public sealed class ProjectGitEvidenceSnapshotCoverage
{
    public Guid SnapshotId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid GitSyncJobId { get; set; }
    public Guid GitSyncCheckpointId { get; set; }
    public string Category { get; set; } = null!;
    public string ScopeKey { get; set; } = "";
    public long ItemsProcessed { get; set; }
    public DateTimeOffset ScopeCompletedAt { get; set; }
    public DateTimeOffset JobCompletedAt { get; set; }
    public int ReceiptPolicyVersion { get; set; } = 1;
}
