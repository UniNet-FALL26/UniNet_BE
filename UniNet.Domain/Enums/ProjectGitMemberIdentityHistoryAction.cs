namespace UniNet.Domain.Enums;

public enum ProjectGitMemberIdentityHistoryAction : short
{
    Linked = 0,
    Unlinked = 1,
    DeactivatedByDeparture = 2,
    DeactivatedByRemoval = 3,
    Reactivated = 4,
    UsernameUpdated = 5
}
