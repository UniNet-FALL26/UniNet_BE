namespace UniNet.Domain.Enums;

public enum GitHubOAuthFlowStatus : short
{
    AwaitingCallback = 0,
    Processing = 1,
    AwaitingCompletion = 2,
    Completed = 3,
    Failed = 4,
    Denied = 5,
    Expired = 6
}
