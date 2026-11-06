namespace UniNet.Domain.Enums;

public enum ProjectGitRepositoryHistoryAction : short
{
    Linked = 0,
    Unlinked = 1,
    CredentialReplaced = 2,
    AccessLost = 3,
    AccessRestored = 4,
    MetadataChanged = 5,
    AuthorizationPending = 6
}
