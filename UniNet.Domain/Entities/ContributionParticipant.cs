using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ContributionParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid UserId { get; set; }
    public string RoleAtSnapshot { get; set; } = null!;
    public ProjectMemberStatus MembershipStatusAtSnapshot { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public bool IsLeader { get; set; }

    public ContributionCycle Cycle { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
