using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class GitSyncCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SyncJobId { get; set; }
    public string Category { get; set; } = null!;
    public string ScopeKey { get; set; } = "";
    public string? Cursor { get; set; }
    public GitSyncCheckpointStatus Status { get; set; } = GitSyncCheckpointStatus.Pending;
    public long ItemsProcessed { get; set; }
    public DateTimeOffset? LastProcessedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
