using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class GitSyncJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid GitHubRepositoryId { get; set; }
    public Guid OAuthConnectionId { get; set; }
    public Guid? RequestedByProfileId { get; set; }
    public GitSyncType SyncType { get; set; }
    public GitSyncJobStatus Status { get; set; } = GitSyncJobStatus.Pending;
    public string? IdempotencyKey { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
