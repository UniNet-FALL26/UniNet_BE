namespace UniNet.Domain.Entities;

// One row per process; timestamps describe successful cycles, not job contents.
public sealed class GitHubAppWorkerHeartbeat
{
    public Guid Id { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? WorkerAt { get; set; }
    public DateTimeOffset? ConsumerAt { get; set; }
    public DateTimeOffset? ReconciliationAt { get; set; }
}
