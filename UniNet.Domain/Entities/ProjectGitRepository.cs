using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectGitRepository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid GitHubRepositoryId { get; set; }
    public Guid OAuthConnectionId { get; set; }
    public Guid LinkedByProfileId { get; set; }
    public ProjectGitRepositoryAccessStatus AccessStatus { get; set; } = ProjectGitRepositoryAccessStatus.Active;
    public bool SyncPaused { get; set; }
    public int SyncIntervalHours { get; set; } = 6;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UnlinkedAt { get; set; }
    public DateTimeOffset? LastAccessCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}
