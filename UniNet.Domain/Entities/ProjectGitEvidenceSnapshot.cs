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
