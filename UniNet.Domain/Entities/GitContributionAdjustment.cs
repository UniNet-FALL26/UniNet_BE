namespace UniNet.Domain.Entities;
public sealed class GitContributionAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid RequestId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExecutionDeadline { get; set; }
    public Guid? GitSnapshotId { get; set; }
    public Guid? PeerCohortId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? BasePublishedRunId { get; set; }
    public string State { get; set; } = "Syncing";
    public int Revision { get; set; }
}
public sealed class GitContributionAdjustmentRepository
{
    public Guid AdjustmentId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid AssociationId { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid JobId { get; set; }
}
