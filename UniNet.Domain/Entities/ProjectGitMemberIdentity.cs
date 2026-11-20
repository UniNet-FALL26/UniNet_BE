using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectGitMemberIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserProfileId { get; set; }
    public string Provider { get; set; } = "github";
    public long ExternalAccountId { get; set; }
    public string ExternalUsername { get; set; } = null!;
    public Guid? VerifiedByConnectionId { get; set; }
    public ProjectGitMemberIdentityStatus Status { get; set; } = ProjectGitMemberIdentityStatus.Active;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UnlinkedAt { get; set; }
    public DateTimeOffset LastActivatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}
