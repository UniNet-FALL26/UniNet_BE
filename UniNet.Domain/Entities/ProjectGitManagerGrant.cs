using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectGitManagerGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserProfileId { get; set; }
    public Guid GrantedByProfileId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByProfileId { get; set; }
}
