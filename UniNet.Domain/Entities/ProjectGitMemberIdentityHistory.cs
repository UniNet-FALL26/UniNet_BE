using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectGitMemberIdentityHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IdentityId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserProfileId { get; set; }
    public string Provider { get; set; } = "github";
    public long ExternalAccountId { get; set; }
    public string ExternalUsername { get; set; } = null!;
    public string? PreviousExternalUsername { get; set; }
    public ProjectGitMemberIdentityHistoryAction Action { get; set; }
    public ProjectGitMemberIdentityStatus? PreviousStatus { get; set; }
    public ProjectGitMemberIdentityStatus? NewStatus { get; set; }
    public Guid PerformedByProfileId { get; set; }
    public Guid? VerifiedByConnectionId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
