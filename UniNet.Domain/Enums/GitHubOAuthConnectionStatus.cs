namespace UniNet.Domain.Enums;

public enum GitHubOAuthConnectionStatus : short
{
    Active = 0,
    ReauthorizationRequired = 1,
    Revoked = 2
}
